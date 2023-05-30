namespace Flow.Plugin.RegJump

type RegQueryResult =
    | ExistingKey of RegPath
    | NonExistingKey of string
    | InaccessibleKey of string
    | InvalidKey

and RegPath = { Key : RegKey; SubKeys : RegKey array }
and RegKey = { KeyName : string; KeyFullPath : string }

[<RequireQualifiedAccess>]
module Registry =

    open Microsoft.Win32
    open System.IO

    let hives = [
        { KeyName = "HKCR" ; KeyFullPath = "HKEY_CLASSES_ROOT" }, RegistryHive.ClassesRoot
        { KeyName = "HKCU" ; KeyFullPath = "HKEY_CURRENT_USER" }, RegistryHive.CurrentUser
        { KeyName = "HKLM" ; KeyFullPath = "HKEY_LOCAL_MACHINE" }, RegistryHive.LocalMachine
        { KeyName = "HKU"  ; KeyFullPath = "HKEY_USERS" }, RegistryHive.Users
        { KeyName = "HKCC" ; KeyFullPath = "HKEY_CURRENT_CONFIG" }, RegistryHive.CurrentConfig
    ]

    let tryPath (path: string) =
        let split = path.Replace("/", @"\").Split(@"\")

        let hiveOpt =
            Array.tryHead split
            |> Option.map (fun str -> str.ToUpper())
            |> Option.bind (fun str ->
                hives |> List.tryFind (fun (hiveKey, _) -> hiveKey.KeyName = str || hiveKey.KeyFullPath = str)
            )

        match hiveOpt with
        | Some (hiveKey, hive) ->
            let subKeyPath = Path.Combine(Array.tail split)
            let fullPath = Path.Combine(hiveKey.KeyName, subKeyPath)
            try
                use key =
                    RegistryKey
                        .OpenBaseKey(hive, RegistryView.Default)
                        .OpenSubKey(subKeyPath)

                if isNull key then
                    NonExistingKey (Path.Combine (hiveKey.KeyName, subKeyPath))
                else
                    ExistingKey {
                        Key = { 
                            KeyName = Array.last split
                            KeyFullPath = fullPath 
                        }
                        SubKeys =
                            key.GetSubKeyNames()
                            |> Array.map (fun subKey -> { KeyName = subKey; KeyFullPath = Path.Combine(fullPath, subKey) })
                    }

            with :? System.Security.SecurityException ->
                InaccessibleKey fullPath

        | None ->
            InvalidKey

open Flow.Launcher.Plugin
open Flow.Launcher.Plugin.SharedCommands
open System.Collections.Generic
open System.Diagnostics

type RegJumpPlugin() =

    let mutable pluginContext = PluginInitContext()

    let regJump (key : RegKey) =
        try
            do ProcessStartInfo(
                    FileName = "regjump.exe",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    Arguments = key.KeyFullPath
                )
            |> ShellCommand.Execute
        with _ ->
            ()

        true

    let changeQuery (key : RegKey) =
        do pluginContext.API.ChangeQuery $"{pluginContext.CurrentPluginMetadata.ActionKeyword} {key.KeyFullPath}"

        false

    let jumpOrChangeQuery (key : RegKey) (ctx : ActionContext) =
        if ctx.SpecialKeyState.CtrlPressed then
            regJump key
        else
            changeQuery key

    interface IPlugin with
        member this.Init (context: PluginInitContext) =
            pluginContext <- context

        member this.Query(query : Query) =
            let results =
                match Registry.tryPath query.Search with
                | ExistingKey details ->
                    [
                        Result (
                            Title = details.Key.KeyFullPath,
                            SubTitle = details.Key.KeyFullPath,
                            IcoPath = "icon.png",
                            AutoCompleteText = $"{pluginContext.CurrentPluginMetadata.ActionKeyword} {details.Key.KeyFullPath}",
                            CopyText = details.Key.KeyFullPath,
                            Score = 1000,
                            Action = fun _ -> regJump details.Key
                        )
                        for subKey in details.SubKeys do
                            Result (
                                Title = $"sub key: {subKey.KeyName}",
                                SubTitle = subKey.KeyFullPath,
                                IcoPath = "icon.png",
                                AutoCompleteText = $"{pluginContext.CurrentPluginMetadata.ActionKeyword} {subKey.KeyFullPath}",
                                CopyText = subKey.KeyFullPath,
                                Action = jumpOrChangeQuery subKey
                            )
                    ]
                | NonExistingKey keyPath ->
                    [
                        Result (
                            Title = "Registry key not found",
                            SubTitle = keyPath,
                            IcoPath = "icon.png"
                        )
                    ]
                | InaccessibleKey keyPath ->
                    [
                        Result (
                            Title = "Registry key is not accessible",
                            SubTitle = keyPath,
                            IcoPath = "icon.png"
                        )
                    ]
                | InvalidKey ->
                    [ for (hiveKey, _) in Registry.hives ->
                        Result (
                            Title = hiveKey.KeyName,
                            SubTitle = hiveKey.KeyFullPath,
                            IcoPath = "icon.png",
                            AutoCompleteText = $"{pluginContext.CurrentPluginMetadata.ActionKeyword} {hiveKey.KeyFullPath}",
                            Action = jumpOrChangeQuery hiveKey
                        )
                    ]

            List<Result> results
