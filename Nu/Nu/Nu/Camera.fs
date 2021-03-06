﻿namespace Nu
open OpenTK
open Nu

[<AutoOpen>]
module CameraModule =

    /// The camera used to dictate what is rendered on the screen.
    ///
    /// Due to the complexity of implementing view scaling using the SDL drawing primitives, Nu has
    /// opted to be a pixel-perfect game engine without scaling. Once Nu's renderer is replaced
    /// with direct calls to OpenGL, scaling will likely be implemented.
    type [<StructuralEquality; NoComparison>] Camera =
        { EyeCenter : Vector2
          EyeSize : Vector2 }

module Camera =

    let getViewAbsoluteF camera =
        Matrix3.identity
        
    let getViewAbsoluteI camera =
        Matrix3.identity

    /// The relative view of the camera with original float values. Due to the problems with
    /// SDL_RenderCopyEx as described in NuMath.fs, using this function to decide on sprite
    /// coordinates is very, very bad for rendering.
    let getViewRelativeF camera =
        let translation = camera.EyeCenter
        Matrix3.makeFromTranslation translation

    /// The relative view of the camera with translation sliced on integers. Good for rendering.
    let getViewRelativeI camera =
        let translation = camera.EyeCenter
        let translationI = Vector2 (single <| int translation.X, single <| int translation.Y)
        Matrix3.makeFromTranslation translationI