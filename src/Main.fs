namespace Flow.Plugin.RegJump

open Flow.Launcher.Plugin
open System.Collections.Generic

type RegJumpPlugin() =

    let mutable pluginContext = PluginInitContext()

    interface IPlugin with
        member this.Init (context: PluginInitContext) =
            pluginContext <- context

        member this.Query(query : Query) =
            List<Result> []
