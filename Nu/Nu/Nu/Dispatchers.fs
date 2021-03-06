﻿namespace Nu
open System
open OpenTK
open Prime
open TiledSharp
open Nu
open Nu.NuCore
open Nu.NuConstants
open Nu.NuMath
open Nu.Physics
open Nu.Metadata
open Nu.Entity
open Nu.WorldPrims

[<AutoOpen>]
module DispatchersModule =

    type Entity with
        
        (* button xfields *)
        [<XField>] member this.IsDown with get () = this?IsDown () : bool
        member this.SetIsDown (value : bool) : Entity = this?IsDown <- value
        [<XField>] member this.UpSprite with get () = this?UpSprite () : Sprite
        member this.SetUpSprite (value : Sprite) : Entity = this?UpSprite <- value
        [<XField>] member this.DownSprite with get () = this?DownSprite () : Sprite
        member this.SetDownSprite (value : Sprite) : Entity = this?DownSprite <- value
        [<XField>] member this.ClickSound with get () = this?ClickSound () : Sound
        member this.SetClickSound (value : Sound) : Entity = this?ClickSound <- value

        (* label xfields *)
        [<XField>] member this.LabelSprite with get () = this?LabelSprite () : Sprite
        member this.SetLabelSprite (value : Sprite) : Entity = this?LabelSprite <- value

        (* text box xfields *)
        [<XField>] member this.BoxSprite with get () = this?BoxSprite () : Sprite
        member this.SetBoxSprite (value : Sprite) : Entity = this?BoxSprite <- value
        [<XField>] member this.Text with get () = this?Text () : string
        member this.SetText (value : string) : Entity = this?Text <- value
        [<XField>] member this.TextFont with get () = this?TextFont () : Font
        member this.SetTextFont (value : Font) : Entity = this?TextFont <- value
        [<XField>] member this.TextOffset with get () = this?TextOffset () : Vector2
        member this.SetTextOffset (value : Vector2) : Entity = this?TextOffset <- value
        [<XField>] member this.TextColor with get () = this?TextColor () : Vector4
        member this.SetTextColor (value : Vector4) : Entity = this?TextColor <- value

        (* toggle xfields *)
        [<XField>] member this.IsOn with get () = this?IsOn () : bool
        member this.SetIsOn (value : bool) : Entity = this?IsOn <- value
        [<XField>] member this.IsPressed with get () = this?IsPressed () : bool
        member this.SetIsPressed (value : bool) : Entity = this?IsPressed <- value
        [<XField>] member this.OffSprite with get () = this?OffSprite () : Sprite
        member this.SetOffSprite (value : Sprite) : Entity = this?OffSprite <- value
        [<XField>] member this.OnSprite with get () = this?OnSprite () : Sprite
        member this.SetOnSprite (value : Sprite) : Entity = this?OnSprite <- value
        [<XField>] member this.ToggleSound with get () = this?ToggleSound () : Sound
        member this.SetToggleSound (value : Sound) : Entity = this?ToggleSound <- value

        (* feeler xfields *)
        [<XField>] member this.IsTouched with get () = this?IsTouched () : bool
        member this.SetIsTouched (value : bool) : Entity = this?IsTouched <- value

        (* fill bar xfields *)
        [<XField>] member this.Fill with get () = this?Fill () : single
        member this.SetFill (value : single) : Entity = this?Fill <- value
        [<XField>] member this.FillInset with get () = this?FillInset () : single
        member this.SetFillInset (value : single) : Entity = this?FillInset <- value
        [<XField>] member this.FillSprite with get () = this?FillSprite () : Sprite
        member this.SetFillSprite (value : Sprite) : Entity = this?FillSprite <- value
        [<XField>] member this.BorderSprite with get () = this?BorderSprite () : Sprite
        member this.SetBorderSprite (value : Sprite) : Entity = this?BorderSprite <- value

        (* block xfields *)
        [<XField>] member this.PhysicsId with get () = this?PhysicsId () : PhysicsId
        member this.SetPhysicsId (value : PhysicsId) : Entity = this?PhysicsId <- value
        [<XField>] member this.Density with get () = this?Density () : single
        member this.SetDensity (value : single) : Entity = this?Density <- value
        [<XField>] member this.BodyType with get () = this?BodyType () : BodyType
        member this.SetBodyType (value : BodyType) : Entity = this?BodyType <- value
        [<XField>] member this.ImageSprite with get () = this?ImageSprite () : Sprite
        member this.SetImageSprite (value : Sprite) : Entity = this?ImageSprite <- value

        (* avatar xfields *)
        // uses same xfields as block

        (* tile map xfields *)
        [<XField>] member this.PhysicsIds with get () = this?PhysicsIds () : PhysicsId list
        member this.SetPhysicsIds (value : PhysicsId list) : Entity = this?PhysicsIds <- value
        [<XField>] member this.TileMapAsset with get () = this?TileMapAsset () : TileMapAsset
        member this.SetTileMapAsset (value : TileMapAsset) : Entity = this?TileMapAsset <- value

    type ButtonDispatcher () =
        inherit Entity2dDispatcher ()

        let handleButtonEventDownMouseLeft event publisher subscriber message world =
            match message.Data with
            | MouseButtonData (mousePosition, _) ->
                let button = get world <| worldEntityLens subscriber
                let mousePositionButton = Entity.mouseToEntity mousePosition world button
                if button.Enabled && button.Visible then
                    if isInBox3 mousePositionButton button.Position button.Size then
                        let button' = button.SetIsDown true
                        let world' = set button' world <| worldEntityLens subscriber
                        let (keepRunning, world'') = publish (straddr "Down" subscriber) subscriber { Handled = false; Data = NoData } world'
                        (handleMessage message, keepRunning, world'')
                    else (message, true, world)
                else (message, true, world)
            | _ -> failwith ("Expected MouseButtonData from event '" + addrToStr event + "'.")

        let handleButtonEventUpMouseLeft event publisher subscriber message world =
            match message.Data with
            | MouseButtonData (mousePosition, _) ->
                let button = get world <| worldEntityLens subscriber
                let mousePositionButton = Entity.mouseToEntity mousePosition world button
                if button.Enabled && button.Visible then
                    let (keepRunning, world') =
                        let button' = button.SetIsDown false
                        let world'' = set button' world <| worldEntityLens subscriber
                        publish (straddr "Up" subscriber) subscriber { Handled = false; Data = NoData } world''
                    if keepRunning && isInBox3 mousePositionButton button.Position button.Size && button.IsDown then
                        let (keepRunning', world'') = publish (straddr "Click" subscriber) subscriber { Handled = false; Data = NoData } world'
                        let sound = PlaySound { Volume = 1.0f; Sound = button.ClickSound }
                        let world'3 = { world'' with AudioMessages = sound :: world''.AudioMessages }
                        (handleMessage message, keepRunning', world'3)
                    else (message, keepRunning, world')
                else (message, true, world)
            | _ -> failwith ("Expected MouseButtonData from event '" + addrToStr event + "'.")

        override dispatcher.Init (button, dispatcherContainer) =
            let button' = base.Init (button, dispatcherContainer)
            button'
                .SetIsDown(false)
                .SetUpSprite({ SpriteAssetName = "Image"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetDownSprite({ SpriteAssetName = "Image2"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetClickSound({ SoundAssetName = "Sound"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.Register (button, address, world) =
            let world' =
                world |>
                    subscribe DownMouseLeftEvent address (CustomSub handleButtonEventDownMouseLeft) |>
                    subscribe UpMouseLeftEvent address (CustomSub handleButtonEventUpMouseLeft)
            (button, world')

        override dispatcher.Unregister (button, address, world) =
            world |>
                unsubscribe DownMouseLeftEvent address |>
                unsubscribe UpMouseLeftEvent address

        override dispatcher.GetRenderDescriptors (button, viewAbsolute, viewRelative, world) =
            if not button.Visible then []
            else
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = button.Position * viewAbsolute
                              Size = button.Size
                              Rotation = 0.0f
                              Sprite = if button.IsDown then button.DownSprite else button.UpSprite
                              Color = Vector4.One }
                          Depth = button.Depth }]

        override dispatcher.GetQuickSize (button, world) =
            let sprite = button.UpSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size

        override dispatcher.IsTransformRelative (_, _) =
            false

    type LabelDispatcher () =
        inherit Entity2dDispatcher ()
            
        override dispatcher.Init (label, dispatcherContainer) =
            let label' = base.Init (label, dispatcherContainer)
            label'.SetLabelSprite({ SpriteAssetName = "Image4"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.GetRenderDescriptors (label, viewAbsolute, viewRelative, world) =
            if not label.Visible then []
            else
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = label.Position * viewAbsolute
                              Size = label.Size
                              Rotation = 0.0f
                              Sprite = label.LabelSprite
                              Color = Vector4.One }
                          Depth = label.Depth }]

        override dispatcher.GetQuickSize (label, world) =
            let sprite = label.LabelSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size

        override dispatcher.IsTransformRelative (_, _) =
            false

    type TextBoxDispatcher () =
        inherit Entity2dDispatcher ()
            
        override dispatcher.Init (textBox, dispatcherContainer) =
            let textBox' = base.Init (textBox, dispatcherContainer)
            textBox'
                .SetBoxSprite({ SpriteAssetName = "Image4"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetText(String.Empty)
                .SetTextFont({ FontAssetName = "Font"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetTextOffset(Vector2.Zero)
                .SetTextColor(Vector4.One)

        override dispatcher.GetRenderDescriptors (textBox, viewAbsolute, viewRelative, world) =
            if not textBox.Visible then []
            else
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = textBox.Position * viewAbsolute
                              Size = textBox.Size
                              Rotation = 0.0f
                              Sprite = textBox.BoxSprite
                              Color = Vector4.One }
                          Depth = textBox.Depth }
                 LayerableDescriptor <|
                    LayeredTextDescriptor
                        { Descriptor =
                            { Text = textBox.Text
                              Position = (textBox.Position + textBox.TextOffset) * viewAbsolute
                              Size = textBox.Size - textBox.TextOffset
                              Font = textBox.TextFont
                              Color = textBox.TextColor }
                          Depth = textBox.Depth }]

        override dispatcher.GetQuickSize (textBox, world) =
            let sprite = textBox.BoxSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size

        override dispatcher.IsTransformRelative (_, _) =
            false

    type ToggleDispatcher () =
        inherit Entity2dDispatcher ()

        let handleToggleEventDownMouseLeft event publisher subscriber message world =
            match message.Data with
            | MouseButtonData (mousePosition, _) ->
                let toggle = get world <| worldEntityLens subscriber
                let mousePositionToggle = Entity.mouseToEntity mousePosition world toggle
                if toggle.Enabled && toggle.Visible then
                    if isInBox3 mousePositionToggle toggle.Position toggle.Size then
                        let toggle' = toggle.SetIsPressed true
                        let world' = set toggle' world <| worldEntityLens subscriber
                        (handleMessage message, true, world')
                    else (message, true, world)
                else (message, true, world)
            | _ -> failwith ("Expected MouseButtonData from event '" + addrToStr event + "'.")
    
        let handleToggleEventUpMouseLeft event publisher subscriber message world =
            match message.Data with
            | MouseButtonData (mousePosition, _) ->
                let toggle = get world <| worldEntityLens subscriber
                let mousePositionToggle = Entity.mouseToEntity mousePosition world toggle
                if toggle.Enabled && toggle.Visible && toggle.IsPressed then
                    let toggle' = toggle.SetIsPressed false
                    if isInBox3 mousePositionToggle toggle'.Position toggle'.Size then
                        let toggle'' = toggle'.SetIsOn <| not toggle'.IsOn
                        let world' = set toggle'' world <| worldEntityLens subscriber
                        let messageType = if toggle''.IsOn then "On" else "Off"
                        let (keepRunning, world'') = publish (straddr messageType subscriber) subscriber { Handled = false; Data = NoData } world'
                        let sound = PlaySound { Volume = 1.0f; Sound = toggle''.ToggleSound }
                        let world'3 = { world'' with AudioMessages = sound :: world''.AudioMessages }
                        (handleMessage message, keepRunning, world'3)
                    else
                        let world' = set toggle' world <| worldEntityLens subscriber
                        (message, true, world')
                else (message, true, world)
            | _ -> failwith ("Expected MouseButtonData from event '" + addrToStr event + "'.")
        
        override dispatcher.Init (toggle, dispatcherContainer) =
            let toggle' = base.Init (toggle, dispatcherContainer)
            toggle'
                .SetIsOn(false)
                .SetIsPressed(false)
                .SetOffSprite({ SpriteAssetName = "Image"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetOnSprite({ SpriteAssetName = "Image2"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetToggleSound({ SoundAssetName = "Sound"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.Register (toggle, address, world) =
            let world' =
                world |>
                    subscribe DownMouseLeftEvent address (CustomSub handleToggleEventDownMouseLeft) |>
                    subscribe UpMouseLeftEvent address (CustomSub handleToggleEventUpMouseLeft)
            (toggle, world')

        override dispatcher.Unregister (toggle, address, world) =
            world |>
                unsubscribe DownMouseLeftEvent address |>
                unsubscribe UpMouseLeftEvent address

        override dispatcher.GetRenderDescriptors (toggle, viewAbsolute, viewRelative, world) =
            if not toggle.Visible then []
            else
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = toggle.Position * viewAbsolute
                              Size = toggle.Size
                              Rotation = 0.0f
                              Sprite = if toggle.IsOn || toggle.IsPressed then toggle.OnSprite else toggle.OffSprite
                              Color = Vector4.One }
                          Depth = toggle.Depth }]

        override dispatcher.GetQuickSize (toggle, world) =
            let sprite = toggle.OffSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size

        override dispatcher.IsTransformRelative (_, _) =
            false

    type FeelerDispatcher () =
        inherit Entity2dDispatcher ()

        let handleFeelerEventDownMouseLeft event publisher subscriber message world =
            match message.Data with
            | MouseButtonData (mousePosition, _) as mouseButtonData ->
                let feeler = get world <| worldEntityLens subscriber
                let mousePositionFeeler = Entity.mouseToEntity mousePosition world feeler
                if feeler.Enabled && feeler.Visible then
                    if isInBox3 mousePositionFeeler feeler.Position feeler.Size then
                        let feeler' = feeler.SetIsTouched true
                        let world' = set feeler' world <| worldEntityLens subscriber
                        let (keepRunning, world'') = publish (straddr "Touch" subscriber) subscriber { Handled = false; Data = mouseButtonData } world'
                        (handleMessage message, keepRunning, world'')
                    else (message, true, world)
                else (message, true, world)
            | _ -> failwith ("Expected MouseButtonData from event '" + addrToStr event + "'.")
    
        let handleFeelerEventUpMouseLeft event publisher subscriber message world =
            match message.Data with
            | MouseButtonData _ ->
                let feeler = get world <| worldEntityLens subscriber
                if feeler.Enabled && feeler.Visible then
                    let feeler' = feeler.SetIsTouched false
                    let world' = set feeler' world <| worldEntityLens subscriber
                    let (keepRunning, world'') = publish (straddr "Release" subscriber) subscriber { Handled = false; Data = NoData } world'
                    (handleMessage message, keepRunning, world'')
                else (message, true, world)
            | _ -> failwith ("Expected MouseButtonData from event '" + addrToStr event + "'.")
        
        override dispatcher.Init (feeler, dispatcherContainer) =
            let feeler' = base.Init (feeler, dispatcherContainer)
            feeler'.SetIsTouched(false)

        override dispatcher.Register (feeler, address, world) =
            let world' =
                world |>
                    subscribe DownMouseLeftEvent address (CustomSub handleFeelerEventDownMouseLeft) |>
                    subscribe UpMouseLeftEvent address (CustomSub handleFeelerEventUpMouseLeft)
            (feeler, world')

        override dispatcher.Unregister (feeler, address, world) =
            world |>
                unsubscribe UpMouseLeftEvent address |>
                unsubscribe DownMouseLeftEvent address

        override dispatcher.GetQuickSize (feeler, world) =
            Vector2 64.0f

        override dispatcher.IsTransformRelative (_, _) =
            false

    type FillBarDispatcher () =
        inherit Entity2dDispatcher ()

        let getFillBarSpriteDims (fillBar : Entity) =
            let spriteInset = fillBar.Size * fillBar.FillInset * 0.5f
            let spritePosition = fillBar.Position + spriteInset
            let spriteWidth = (fillBar.Size.X - spriteInset.X * 2.0f) * fillBar.Fill
            let spriteHeight = fillBar.Size.Y - spriteInset.Y * 2.0f
            (spritePosition, Vector2 (spriteWidth, spriteHeight))

        override dispatcher.Init (fillBar, dispatcherContainer) =
            let fillBar' = base.Init (fillBar, dispatcherContainer)
            fillBar'
                .SetFill(0.0f)
                .SetFillInset(0.0f)
                .SetFillSprite({ SpriteAssetName = "Image9"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })
                .SetBorderSprite({ SpriteAssetName = "Image10"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.GetRenderDescriptors (fillBar, viewAbsolute, viewRelative, world) =
            if not fillBar.Visible then []
            else
                let (fillBarSpritePosition, fillBarSpriteSize) = getFillBarSpriteDims fillBar
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = fillBarSpritePosition * viewAbsolute
                              Size = fillBarSpriteSize
                              Rotation = 0.0f
                              Sprite = fillBar.FillSprite
                              Color = Vector4.One }
                          Depth = fillBar.Depth }
                    LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = fillBar.Position * viewAbsolute
                              Size = fillBar.Size
                              Rotation = 0.0f
                              Sprite = fillBar.BorderSprite
                              Color = Vector4.One }
                          Depth = fillBar.Depth }]

        override dispatcher.GetQuickSize (fillBar, world) =
            let sprite = fillBar.BorderSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size

        override dispatcher.IsTransformRelative (_, _) =
            false

    type BlockDispatcher () =
        inherit Entity2dDispatcher ()

        let registerBlockPhysics address (block : Entity) world =
            let block' = block.SetPhysicsId <| getPhysicsId block.Id
            let bodyCreateMessage =
                BodyCreateMessage
                    { EntityAddress = address
                      PhysicsId = block'.PhysicsId
                      Shape = BoxShape
                        { Extent = block'.Size * 0.5f
                          Properties =
                            { Center = Vector2.Zero
                              Restitution = 0.0f
                              FixedRotation = false
                              LinearDamping = 5.0f
                              AngularDamping = 5.0f }}
                      Position = block'.Position + block'.Size * 0.5f
                      Rotation = block'.Rotation
                      Density = block'.Density
                      BodyType = block'.BodyType }
            let world' = { world with PhysicsMessages = bodyCreateMessage :: world.PhysicsMessages }
            (block', world')

        let unregisterBlockPhysics (_ : Address) (block : Entity) world =
            let bodyDestroyMessage = BodyDestroyMessage { PhysicsId = block.PhysicsId }
            { world with PhysicsMessages = bodyDestroyMessage :: world.PhysicsMessages }

        override dispatcher.Init (block, dispatcherContainer) =
            let block' = base.Init (block, dispatcherContainer)
            block'
                .SetPhysicsId(InvalidPhysicsId)
                .SetDensity(NormalDensity)
                .SetBodyType(BodyType.Dynamic)
                .SetImageSprite({ SpriteAssetName = "Image3"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.Register (block, address, world) =
            registerBlockPhysics address block world

        override dispatcher.Unregister (block, address, world) =
            unregisterBlockPhysics address block world
            
        override dispatcher.PropagatePhysics (block, address, world) =
            let (block', world') = world |> unregisterBlockPhysics address block |> registerBlockPhysics address block
            set block' world' <| worldEntityLens address

        override dispatcher.ReregisterPhysicsHack (block, groupAddress, world) =
            let address = addrstr groupAddress block.Name
            let world' = unregisterBlockPhysics address block world
            let (block', world'') = registerBlockPhysics address block world'
            set block' world'' <| worldEntityLens address

        override dispatcher.HandleBodyTransformMessage (block, message, address, world) =
            let block' =
                block
                    .SetPosition(message.Position - block.Size * 0.5f) // TODO: see if this center-offsetting can be encapsulated within the Physics module!
                    .SetRotation(message.Rotation)
            set block' world <| worldEntityLens message.EntityAddress
            
        override dispatcher.GetRenderDescriptors (block, viewAbsolute, viewRelative, world) =
            if not block.Visible then []
            else
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = block.Position * viewRelative
                              Size = block.Size * Matrix3.getScaleMatrix viewAbsolute
                              Rotation = block.Rotation
                              Sprite = block.ImageSprite
                              Color = Vector4.One }
                          Depth = block.Depth }]

        override dispatcher.GetQuickSize (block, world) =
            let sprite = block.ImageSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size
    
    type AvatarDispatcher () =
        inherit Entity2dDispatcher ()

        let registerAvatarPhysics address (avatar : Entity) world =
            let avatar' = avatar.SetPhysicsId <| getPhysicsId avatar.Id
            let bodyCreateMessage =
                BodyCreateMessage
                    { EntityAddress = address
                      PhysicsId = avatar'.PhysicsId
                      Shape =
                        CircleShape
                            { Radius = avatar'.Size.X * 0.5f
                              Properties =
                                { Center = Vector2.Zero
                                  Restitution = 0.0f
                                  FixedRotation = true
                                  LinearDamping = 10.0f
                                  AngularDamping = 0.0f }}
                      Position = avatar'.Position + avatar'.Size * 0.5f
                      Rotation = avatar'.Rotation
                      Density = avatar'.Density
                      BodyType = BodyType.Dynamic }
            let world' = { world with PhysicsMessages = bodyCreateMessage :: world.PhysicsMessages }
            (avatar', world')

        let unregisterAvatarPhysics (_ : Address) (avatar : Entity) world =
            let bodyDestroyMessage = BodyDestroyMessage { PhysicsId = avatar.PhysicsId }
            { world with PhysicsMessages = bodyDestroyMessage :: world.PhysicsMessages }

        override dispatcher.Init (avatar, dispatcherContainer) =
            let avatar' = base.Init (avatar, dispatcherContainer)
            avatar'
                .SetPhysicsId(InvalidPhysicsId)
                .SetDensity(NormalDensity)
                .SetImageSprite({ SpriteAssetName = "Image7"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.Register (avatar, address, world) =
            registerAvatarPhysics address avatar world

        override dispatcher.Unregister (avatar, address, world) =
            unregisterAvatarPhysics address avatar world
            
        override dispatcher.PropagatePhysics (avatar, address, world) =
            let (avatar', world') = world |> unregisterAvatarPhysics address avatar |> registerAvatarPhysics address avatar
            set avatar' world' <| worldEntityLens address

        override dispatcher.ReregisterPhysicsHack (avatar, groupAddress, world) =
            let address = addrstr groupAddress avatar.Name
            let world' = unregisterAvatarPhysics address avatar world
            let (avatar', world'') = registerAvatarPhysics address avatar world'
            set avatar' world'' <| worldEntityLens address

        override dispatcher.HandleBodyTransformMessage (avatar, message, address, world) =
            let avatar' =
                (avatar
                    .SetPosition <| message.Position - avatar.Size * 0.5f) // TODO: see if this center-offsetting can be encapsulated within the Physics module!
                    .SetRotation message.Rotation
            set avatar' world <| worldEntityLens message.EntityAddress

        override dispatcher.GetRenderDescriptors (avatar, viewAbsolute, viewRelative, world) =
            if not avatar.Visible then []
            else
                [LayerableDescriptor <|
                    LayeredSpriteDescriptor
                        { Descriptor =
                            { Position = avatar.Position * viewRelative
                              Size = avatar.Size * Matrix3.getScaleMatrix viewAbsolute
                              Rotation = avatar.Rotation
                              Sprite = avatar.ImageSprite
                              Color = Vector4.One }
                          Depth = avatar.Depth }]

        override dispatcher.GetQuickSize (avatar, world) =
            let sprite = avatar.ImageSprite
            match tryGetTextureSizeAsVector2 sprite.SpriteAssetName sprite.PackageName world.AssetMetadataMap with
            | None -> DefaultEntitySize
            | Some size -> size

    type TileMapDispatcher () =
        inherit Entity2dDispatcher ()

        let registerTilePhysics tileMap tmd tld address n (world, physicsIds) (_ : TmxLayerTile) =
            let td = makeTileData tileMap tmd tld n
            match td.OptTileSetTile with
            | None -> (world, physicsIds)
            | Some tileSetTile when not <| tileSetTile.Properties.ContainsKey "c" -> (world, physicsIds)
            | Some tileSetTile ->
                let physicsId = getPhysicsId tileMap.Id
                let boxShapeProperties =
                    { Center = Vector2.Zero
                      Restitution = 0.0f
                      FixedRotation = true
                      LinearDamping = 0.0f
                      AngularDamping = 0.0f }
                let bodyCreateMessage =
                    BodyCreateMessage
                        { EntityAddress = address
                          PhysicsId = physicsId
                          Shape = BoxShape { Extent = Vector2 (single <| fst tmd.TileSize, single <| snd tmd.TileSize) * 0.5f; Properties = boxShapeProperties }
                          Position = Vector2 (single <| fst td.TilePosition + fst tmd.TileSize / 2, single <| snd td.TilePosition + snd tmd.TileSize / 2 + snd tmd.TileMapSize)
                          Rotation = tileMap.Rotation
                          Density = tileMap.Density
                          BodyType = BodyType.Static }
                let world' = { world with PhysicsMessages = bodyCreateMessage :: world.PhysicsMessages }
                (world', physicsId :: physicsIds)

        let registerTileMapPhysics address (tileMap : Entity) world =
            let collisionLayer = 0 // MAGIC_VALUE: assumption
            let tmd = makeTileMapData tileMap.TileMapAsset world
            let tld = makeTileLayerData tileMap tmd collisionLayer
            let (world', physicsIds) = Seq.foldi (registerTilePhysics tileMap tmd tld address) (world, []) tld.Tiles
            let tileMap' = tileMap.SetPhysicsIds physicsIds
            (tileMap', world')

        let unregisterTilePhysics world physicsId =
            let bodyDestroyMessage = BodyDestroyMessage { PhysicsId = physicsId }
            { world with PhysicsMessages = bodyDestroyMessage :: world.PhysicsMessages }

        let unregisterTileMapPhysics (_ : Address) (tileMap : Entity) world =
            List.fold unregisterTilePhysics world <| tileMap.PhysicsIds
        
        override dispatcher.Init (tileMap, dispatcherContainer) =
            let tileMap' = base.Init (tileMap, dispatcherContainer)
            tileMap'
                .SetPhysicsIds([])
                .SetDensity(NormalDensity)
                .SetTileMapAsset({ TileMapAssetName = "TileMap"; PackageName = DefaultPackageName; PackageFileName = AssetGraphFileName })

        override dispatcher.Register (tileMap, address, world) =
            registerTileMapPhysics address tileMap world

        override dispatcher.Unregister (tileMap, address, world) =
            unregisterTileMapPhysics address tileMap world
            
        override dispatcher.PropagatePhysics (tileMap, address, world) =
            let (tileMap', world') = world |> unregisterTileMapPhysics address tileMap |> registerTileMapPhysics address tileMap
            set tileMap' world' <| worldEntityLens address

        override dispatcher.ReregisterPhysicsHack (tileMap, groupAddress, world) =
            let address = addrstr groupAddress tileMap.Name
            let world' = unregisterTileMapPhysics address tileMap world
            let (tileMap', world'') = registerTileMapPhysics address tileMap world'
            set tileMap' world'' <| worldEntityLens address

        override dispatcher.GetRenderDescriptors (tileMap, viewAbsolute, viewRelative, world) =
            if not tileMap.Visible then []
            else
                let tileMapAsset = tileMap.TileMapAsset
                match tryGetTileMapMetadata tileMapAsset.TileMapAssetName tileMapAsset.PackageName world.AssetMetadataMap with
                | None -> []
                | Some (_, sprites, map) ->
                    let layers = List.ofSeq map.Layers
                    let viewScale = Matrix3.getScaleMatrix viewRelative
                    let tileSourceSize = (map.TileWidth, map.TileHeight)
                    let tileSize = Vector2 (single map.TileWidth, single map.TileHeight) * viewScale
                    List.mapi
                        (fun i (layer : TmxLayer) ->
                            let layeredTileLayerDescriptor =
                                LayeredTileLayerDescriptor
                                    { Descriptor =
                                        { Position = tileMap.Position * viewRelative
                                          Size = Vector2.Zero
                                          Rotation = tileMap.Rotation
                                          MapSize = (map.Width, map.Height)
                                          Tiles = layer.Tiles
                                          TileSourceSize = tileSourceSize
                                          TileSize = tileSize
                                          TileMapSize = Vector2 (tileSize.X * single map.Width, tileSize.Y * single map.Height)
                                          TileSet = map.Tilesets.[0] // MAGIC_VALUE: I have no idea how to tell which tile set each tile is from...
                                          TileSetSprite = List.head sprites } // MAGIC_VALUE: for same reason as above
                                      Depth = tileMap.Depth + single i * 2.0f } // MAGIC_VALUE: assumption
                            LayerableDescriptor layeredTileLayerDescriptor)
                        layers

        override dispatcher.GetQuickSize (tileMap, world) =
            let tileMapAsset = tileMap.TileMapAsset
            match tryGetTileMapMetadata tileMapAsset.TileMapAssetName tileMapAsset.PackageName world.AssetMetadataMap with
            | None -> failwith "Unexpected match failure in Nu.World.TileMapDispatcher.GetQuickSize."
            | Some (_, _, map) -> Vector2 (single <| map.Width * map.TileWidth, single <| map.Height * map.TileHeight)