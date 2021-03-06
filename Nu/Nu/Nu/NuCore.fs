﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2014.

namespace Nu
open System
open System.Configuration
open Prime

[<AutoOpen>]
module NuCoreModule =

    /// Specifies the address of an element in a game.
    /// Note that subscribing to a partial address results in listening to all messages whose
    /// beginning address nodes match the partial address (sort of a wild-card).
    type Address = string list

    /// Specifies the screen-clearing routine.
    type ScreenClear =
        | NoClear
        | ColorClear of byte * byte * byte

module NuCore =

    /// The invalid Id.
    /// TODO: ensure this will never be generated by Guid.NewGuid ().
    let InvalidId = Guid.Empty
    
    /// Create a Nu Id.
    let getNuId = Guid.NewGuid

    let addr (str : string) : Address =
        List.ofArray <| str.Split '/'

    let straddr str (address : Address) : Address =
        addr str @ address

    let addrstr (address : Address) str : Address =
        address @ [str]

    let straddrstr str (address : Address) str2 : Address =
        addr str @ address @ addr str2

    let addrToStr (address : Address) =
        List.fold (fun str (sub : string) -> str + sub) String.Empty address

    let (</>) str str2 =
        str + "/" + str2

    let getResolutionOrDefault isX defaultResolution =
        let resolution = ref 0
        let appSetting = ConfigurationManager.AppSettings.["Resolution" + if isX then "X" else "Y"]
        if not <| Int32.TryParse (appSetting, resolution) then resolution := defaultResolution
        !resolution