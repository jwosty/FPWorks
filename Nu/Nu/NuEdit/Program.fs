﻿// NuEdit - The Nu Game Engine editor.
// Copyright (C) Bryan Edds, 2013-2014.

namespace NuEdit
open NuEditDesign
open SDL2
open OpenTK
open TiledSharp
open Prime
open System
open System.IO
open System.Collections.Generic
open System.Reflection
open System.Windows.Forms
open System.ComponentModel
open System.Xml
open System.Xml.Serialization
open Microsoft.FSharp.Reflection
open Prime
open Nu
open Nu.NuCore
open Nu.NuConstants
open Nu.NuMath
open Nu.Metadata
open Nu.Physics
open Nu.Sdl
open Nu.Sim
open Nu.Entity
open Nu.Group
open Nu.Screen
open Nu.Game
open Nu.WorldPrims
open Nu.World
open NuEdit.NuEditConstants
open NuEdit.NuEditReflection

// TODO: increase warning level to 5.

[<AutoOpen>]
module ProgramModule =

    type WorldChanger = World -> World
    type WorldChangers = WorldChanger List

    type DragEntityState =
        | DragEntityNone
        | DragEntityPosition of Vector2 * Vector2 * Address
        | DragEntityRotation of Vector2 * Vector2 * Address

    type DragCameraState =
        | DragCameraNone
        | DragCameraPosition of Vector2 * Vector2

    type EditorState =
        { DragEntityState : DragEntityState
          DragCameraState : DragCameraState
          OptGameDispatcherDescriptor : (string * string) option
          PastWorlds : World list
          FutureWorlds : World list
          Clipboard : (Entity option) ref }

module Program =

    let DefaultPositionSnap = 8
    let DefaultRotationSnap = 5
    let DefaultCreationDepth = 0.0f
    let CameraSpeed = 4.0f // NOTE: might be nice to be able to configure this just like entity creation depth in the editor

    let pushPastWorld pastWorld world =
        let editorState = world.ExtData :?> EditorState
        let editorState' = { editorState with PastWorlds = pastWorld :: editorState.PastWorlds; FutureWorlds = [] }
        { world with ExtData = editorState' }

    let clearOtherWorlds world =
        let editorState = world.ExtData :?> EditorState
        let editorState' = { editorState with PastWorlds = []; FutureWorlds = [] }
        { world with ExtData = editorState' }

    let populateEntityDispatcherComboBox (form : NuEditForm) =
        form.createEntityComboBox.Items.Clear ()
        let entityDispatcherType = typeof<EntityDispatcher>
        for assembly in AppDomain.CurrentDomain.GetAssemblies () do
            for aType in assembly.DefinedTypes do
                if aType.IsSubclassOf entityDispatcherType && not aType.IsAbstract then
                    ignore <| form.createEntityComboBox.Items.Add aType.Name

    type [<TypeDescriptionProvider (typeof<EntityTypeDescriptorProvider>)>] EntityTypeDescriptorSource =
        { Address : Address
          Form : NuEditForm
          WorldChangers : WorldChangers
          RefWorld : World ref }

    and EntityPropertyDescriptor (property) =
        inherit PropertyDescriptor ((match property with EntityXFieldDescriptor x -> x.FieldName | EntityPropertyInfo p -> p.Name), Array.empty)

        let propertyName = match property with EntityXFieldDescriptor x -> x.FieldName | EntityPropertyInfo p -> p.Name
        let propertyType = match property with EntityXFieldDescriptor x -> findType x.TypeName | EntityPropertyInfo p -> p.PropertyType
        let propertyCanWrite = match property with EntityXFieldDescriptor x -> true | EntityPropertyInfo x -> x.CanWrite

        override this.ComponentType with get () = propertyType.DeclaringType
        override this.PropertyType with get () = propertyType
        override this.CanResetValue source = false
        override this.ResetValue source = ()
        override this.ShouldSerializeValue source = true

        override this.IsReadOnly
            with get () =
                not propertyCanWrite ||
                not <| Xtension.isPropertyNameWriteable propertyName

        override this.GetValue optSource =
            match optSource with
            | null -> null
            | source ->
                let entityTds = source :?> EntityTypeDescriptorSource
                let entity = get !entityTds.RefWorld <| worldEntityLens entityTds.Address
                getEntityPropertyValue property entity

        // NOTE: the hard-coded special casing going on in this function is really bad :(
        override this.SetValue (source, value) =
            let entityTds = source :?> EntityTypeDescriptorSource
            let changer = (fun world ->
                let world_ =
                    match propertyName with
                    | "Name" -> // MAGIC_VALUE
                        // handle special case for an entity's Name field change
                        let valueStr = string value
                        if Int64.TryParse (valueStr, ref 0L) then
                            trace <| "Invalid entity name '" + valueStr + "' (must not be a number)."
                            world
                        else
                            // TODO: factor out a renameEntity function
                            let entity_ = get world <| worldEntityLens entityTds.Address
                            let world_ = removeEntity entityTds.Address world
                            let entity_ = { entity_ with Name = valueStr }
                            let entityAddress = addrstr EditorGroupAddress valueStr
                            let world_ = addEntity entityAddress entity_ world_
                            entityTds.RefWorld := world_ // must be set for property grid
                            entityTds.Form.propertyGrid.SelectedObject <- { entityTds with Address = entityAddress }
                            world_
                    | _ ->
                        let world_ = setEntityPropertyValue entityTds.Address property value world
                        let entity_ = get world_ <| worldEntityLens entityTds.Address
                        entity_.PropagatePhysics (entityTds.Address, world_)
                pushPastWorld world world_)
            entityTds.RefWorld := changer !entityTds.RefWorld
            entityTds.WorldChangers.Add changer

        // NOTE: This has to be a static member in order to see the relevant types in the recursive definitions.
        static member GetPropertyDescriptors (aType : Type) optSource =
            let properties = aType.GetProperties (BindingFlags.Instance ||| BindingFlags.Public)
            let properties' = Seq.filter (fun (property : PropertyInfo) -> Seq.isEmpty <| property.GetCustomAttributes<XFieldAttribute> ()) properties
            let optProperty = Seq.tryFind (fun (property : PropertyInfo) -> property.PropertyType = typeof<Xtension>) properties'
            let propertyDescriptors = Seq.map (fun property -> EntityPropertyDescriptor (EntityPropertyInfo property) :> PropertyDescriptor) properties'
            let propertyDescriptors' =
                match (optProperty, optSource) with
                | (None, _) 
                | (_, None) -> propertyDescriptors
                | (Some property, Some entity) ->
                    let xtension = property.GetValue entity :?> Xtension
                    let xFieldDescriptors =
                        Seq.map
                            (fun (xField : KeyValuePair<string, obj>) ->
                                let fieldName = xField.Key
                                let typeName = (xField.Value.GetType ()).FullName
                                let xFieldDescriptor = EntityXFieldDescriptor { FieldName = fieldName; TypeName = typeName }
                                EntityPropertyDescriptor xFieldDescriptor :> PropertyDescriptor)
                            xtension.XFields
                    Seq.append xFieldDescriptors propertyDescriptors
            List.ofSeq propertyDescriptors'

    and EntityTypeDescriptor (optSource : obj) =
        inherit CustomTypeDescriptor ()
        override this.GetProperties _ =
            let propertyDescriptors =
                match optSource with
                | :? EntityTypeDescriptorSource as source ->
                    let entity = get !source.RefWorld <| worldEntityLens source.Address
                    EntityPropertyDescriptor.GetPropertyDescriptors typeof<Entity> <| Some entity
                | _ -> EntityPropertyDescriptor.GetPropertyDescriptors typeof<Entity> None
            PropertyDescriptorCollection (Array.ofList propertyDescriptors)

    and EntityTypeDescriptorProvider () =
        inherit TypeDescriptionProvider ()
        override this.GetTypeDescriptor (_, optSource) =
            EntityTypeDescriptor optSource :> ICustomTypeDescriptor

    let getSnaps (form : NuEditForm) =
        let positionSnap = ref 0
        ignore <| Int32.TryParse (form.positionSnapTextBox.Text, positionSnap)
        let rotationSnap = ref 0
        ignore <| Int32.TryParse (form.rotationSnapTextBox.Text, rotationSnap)
        (!positionSnap, !rotationSnap)
    
    let getCreationDepth (form : NuEditForm) =
        let creationDepth = ref 0.0f
        ignore <| Single.TryParse (form.creationDepthTextBox.Text, creationDepth)
        !creationDepth

    let beginEntityDrag (form : NuEditForm) worldChangers refWorld _ _ _ message world =
        match message.Data with
        | MouseButtonData (mousePosition, _) ->
            if form.interactButton.Checked then (message, true, world)
            else
                let group = get world (worldGroupLens EditorGroupAddress)
                let entities = Map.toValueList (get world <| worldEntitiesLens EditorGroupAddress)
                let mousePosition = world.MouseState.MousePosition
                let optPicked = tryPickEntity mousePosition entities world
                match optPicked with
                | None -> (handleMessage message, true, world)
                | Some entity ->
                    let mousePositionEntity = Entity.mouseToEntity mousePosition world entity
                    let entityAddress = addrstr EditorGroupAddress entity.Name
                    let entityPosition = getEntityPosition entity
                    let dragState = DragEntityPosition (entityPosition + mousePositionEntity, mousePositionEntity, entityAddress)
                    let editorState_ = world.ExtData :?> EditorState
                    let editorState_ = { editorState_ with DragEntityState = dragState }
                    let world_ = { world with ExtData = editorState_ }
                    let world_ = pushPastWorld world world_
                    refWorld := world_ // must be set for property grid
                    form.propertyGrid.SelectedObject <- { Address = entityAddress; Form = form; WorldChangers = worldChangers; RefWorld = refWorld }
                    (handleMessage message, true, world_)
        | _ -> failwith <| "Expected MouseButtonData in message '" + string message + "'."

    let endEntityDrag (form : NuEditForm) _ _ _ message world =
        match message.Data with
        | MouseButtonData (position, _) ->
            if form.interactButton.Checked then (message, true, world)
            else
                let editorState_ = world.ExtData :?> EditorState
                match editorState_.DragEntityState with
                | DragEntityNone -> (handleMessage message, true, world)
                | DragEntityPosition _
                | DragEntityRotation _ ->
                    let editorState_ = { editorState_ with DragEntityState = DragEntityNone }
                    form.propertyGrid.Refresh ()
                    (handleMessage message, true, { world with ExtData = editorState_ })
        | _ -> failwith <| "Expected MouseButtonData in message '" + string message + "'."

    let updateEntityDrag (form : NuEditForm) world =
        let editorState_ = world.ExtData :?> EditorState
        match editorState_.DragEntityState with
        | DragEntityNone -> world
        | DragEntityPosition (pickOffset, mousePositionEntityOrig, address) ->
            let (positionSnap, _) = getSnaps form
            let entity_ = get world <| worldEntityLens address
            let mousePositionEntity = Entity.mouseToEntity world.MouseState.MousePosition world entity_
            let entityPosition = (pickOffset - mousePositionEntityOrig) + (mousePositionEntity - mousePositionEntityOrig)
            let entity_ = setEntityPosition positionSnap entityPosition entity_
            let world_ = set entity_ world <| worldEntityLens address
            let editorState_ = { editorState_ with DragEntityState = DragEntityPosition (pickOffset, mousePositionEntityOrig, address) }
            let world_ = { world_ with ExtData = editorState_ }
            let world_ = entity_.PropagatePhysics (address, world_)
            form.propertyGrid.Refresh ()
            world_
        | DragEntityRotation (pickOffset, origPosition, address) -> world

    let beginCameraDrag (form : NuEditForm) worldChangers refWorld _ _ _ message world =
        match message.Data with
        | MouseButtonData (position, _) ->
            if form.interactButton.Checked then (message, true, world)
            else
                let mousePosition = world.MouseState.MousePosition
                let mousePositionScreen = Entity.mouseToScreen mousePosition world.Camera
                let dragState = DragCameraPosition (world.Camera.EyeCenter + mousePositionScreen, mousePositionScreen)
                let editorState_ = world.ExtData :?> EditorState
                let editorState_ = { editorState_ with DragCameraState = dragState }
                let world_ = { world with ExtData = editorState_ }
                (handleMessage message, true, world_)
        | _ -> failwith <| "Expected MouseButtonData in message '" + string message + "'."

    let endCameraDrag (form : NuEditForm) _ _ _ message world =
        match message.Data with
        | MouseButtonData (position, _) ->
            if form.interactButton.Checked then (message, true, world)
            else
                let editorState_ = world.ExtData :?> EditorState
                match editorState_.DragCameraState with
                | DragCameraNone -> (handleMessage message, true, world)
                | DragCameraPosition _ ->
                    let editorState_ = { editorState_ with DragCameraState = DragCameraNone }
                    (handleMessage message, true, { world with ExtData = editorState_ })
        | _ -> failwith <| "Expected MouseButtonData in message '" + string message + "'."

    let updateCameraDrag (form : NuEditForm) world =
        let editorState_ = world.ExtData :?> EditorState
        match editorState_.DragCameraState with
        | DragCameraNone -> world
        | DragCameraPosition (pickOffset, mousePositionScreenOrig) ->
            let mousePosition = world.MouseState.MousePosition
            let mousePositionScreen = Entity.mouseToScreen mousePosition world.Camera
            let eyeCenter = (pickOffset - mousePositionScreenOrig) + -CameraSpeed * (mousePositionScreen - mousePositionScreenOrig)
            let camera = { world.Camera with EyeCenter = eyeCenter }
            let world' = { world with Camera = camera }
            let editorState_ = { editorState_ with DragCameraState = DragCameraPosition (pickOffset, mousePositionScreenOrig) }
            { world' with ExtData = editorState_ }

    /// Needed for physics system side-effects...
    let physicsHack world =
        let world' = { world with PhysicsMessages = ResetHackMessage :: world.PhysicsMessages }
        reregisterPhysicsHack EditorGroupAddress world'

    let handleExit (form : NuEditForm) _ =
        form.Close ()

    let handleCreate (form : NuEditForm) (worldChangers : WorldChanger List) refWorld atMouse _ =
        let world = !refWorld
        let entityXDispatcherName = form.createEntityComboBox.Text
        try let entity_ = makeDefaultEntity entityXDispatcherName None false world
            let changer = (fun world_ ->
                let (positionSnap, rotationSnap) = getSnaps form
                let mousePositionEntity = Entity.mouseToEntity world.MouseState.MousePosition world entity_
                let entityPosition = if atMouse then mousePositionEntity else world.Camera.EyeCenter
                let entityTransform = { Transform.Position = entityPosition; Depth = getCreationDepth form; Size = DefaultEntitySize; Rotation = DefaultEntityRotation }
                let entity_ = setEntityTransform positionSnap rotationSnap entityTransform entity_
                let entityAddress = addrstr EditorGroupAddress entity_.Name
                let world_ = addEntity entityAddress entity_ world_
                let world_ = pushPastWorld world world_
                refWorld := world_ // must be set for property grid
                form.propertyGrid.SelectedObject <- { Address = entityAddress; Form = form; WorldChangers = worldChangers; RefWorld = refWorld }
                world_)
            refWorld := changer !refWorld
            worldChangers.Add changer
        with exn ->
            ignore <| MessageBox.Show ("Invalid entity XDispatcher name '" + entityXDispatcherName + "'.")

    let handleDelete (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let selectedObject = form.propertyGrid.SelectedObject
        let changer = (fun world ->
            match selectedObject with
            | :? EntityTypeDescriptorSource as entityTds ->
                let world_ = removeEntity entityTds.Address world
                let world_ = pushPastWorld world world_
                form.propertyGrid.SelectedObject <- null
                world_
            | _ -> world)
        refWorld := changer !refWorld
        worldChangers.Add changer

    let handleSave (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        form.saveFileDialog.FileName <- String.Empty
        let saveFileResult = form.saveFileDialog.ShowDialog form
        match saveFileResult with
        | DialogResult.OK ->
            let world = !refWorld
            let editorState = world.ExtData :?> EditorState
            saveFile editorState.OptGameDispatcherDescriptor form.saveFileDialog.FileName world
        | _ -> ()

    let handleOpen (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let openFileResult = form.openFileDialog.ShowDialog form
        match openFileResult with
        | DialogResult.OK ->
            let changer = (fun world ->
                let (optGameDispatcherDescriptor, world_) = loadFile form.openFileDialog.FileName world
                let editorState = { (world.ExtData :?> EditorState) with OptGameDispatcherDescriptor = optGameDispatcherDescriptor }
                let world_ = { world_ with ExtData = editorState }
                let world_ = clearOtherWorlds world_
                populateEntityDispatcherComboBox form
                form.propertyGrid.SelectedObject <- null
                form.interactButton.Checked <- false
                world_)
            refWorld := changer !refWorld
            worldChangers.Add changer
        | _ -> ()

    let handleUndo (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let changer = (fun world ->
            let futureWorld = world
            let editorState_ = world.ExtData :?> EditorState
            match editorState_.PastWorlds with
            | [] -> world
            | pastWorld :: pastWorlds ->
                let world_ = pastWorld
                let world_ = physicsHack world_
                let editorState_ = { editorState_ with PastWorlds = pastWorlds; FutureWorlds = futureWorld :: editorState_.FutureWorlds }
                let world_ = { world_ with ExtData = editorState_ }
                if form.interactButton.Checked then form.interactButton.Checked <- false
                form.propertyGrid.SelectedObject <- null
                world_)
        refWorld := changer !refWorld
        worldChangers.Add changer

    let handleRedo (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let changer = (fun world ->
            let editorState_ = world.ExtData :?> EditorState
            match editorState_.FutureWorlds with
            | [] -> world
            | futureWorld :: futureWorlds ->
                let world_ = futureWorld
                let world_ = physicsHack world_
                let editorState_ = { editorState_ with PastWorlds = world :: editorState_.PastWorlds; FutureWorlds = futureWorlds }
                let world_ = { world_ with ExtData = editorState_ }
                if form.interactButton.Checked then form.interactButton.Checked <- false
                form.propertyGrid.SelectedObject <- null
                world_)
        refWorld := changer !refWorld
        worldChangers.Add changer

    let handleInteractChanged (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        if form.interactButton.Checked then
            let changer = (fun world -> pushPastWorld world world)
            refWorld := changer !refWorld
            worldChangers.Add changer

    let handleCut (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let optEntityTds = form.propertyGrid.SelectedObject
        match optEntityTds with
        | null -> ()
        | :? EntityTypeDescriptorSource as entityTds ->
            let changer = (fun world ->
                let editorState = world.ExtData :?> EditorState
                let entity = get world <| worldEntityLens entityTds.Address
                let world' = removeEntity entityTds.Address world
                editorState.Clipboard := Some entity
                form.propertyGrid.SelectedObject <- null
                world')
            refWorld := changer !refWorld
            worldChangers.Add changer
        | _ -> trace <| "Invalid cut operation (likely a code issue in NuEdit)."
        
    let handleCopy (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let optEntityTds = form.propertyGrid.SelectedObject
        match optEntityTds with
        | null -> ()
        | :? EntityTypeDescriptorSource as entityTds ->
            let entity = get !refWorld <| worldEntityLens entityTds.Address
            let editorState = (!refWorld).ExtData :?> EditorState
            editorState.Clipboard := Some entity
        | _ -> trace <| "Invalid copy operation (likely a code issue in NuEdit)."

    let handlePaste (form : NuEditForm) (worldChangers : WorldChanger List) refWorld atMouse _ =
        let editorState = (!refWorld).ExtData :?> EditorState
        match !editorState.Clipboard with
        | None -> ()
        | Some entity ->
            let changer = (fun world ->
                let (positionSnap, rotationSnap) = getSnaps form
                let id = getNuId ()
                let mousePositionEntity = Entity.mouseToEntity world.MouseState.MousePosition world entity
                let entity_ = { entity with Id = id; Name = string id }
                let entityPosition = if atMouse then mousePositionEntity else world.Camera.EyeCenter
                let entityTransform = { getEntityTransform entity with Position = entityPosition; Depth = getCreationDepth form }
                let entity_ = setEntityTransform positionSnap rotationSnap entityTransform entity_
                let address = addrstr EditorGroupAddress entity_.Name
                let world_ = pushPastWorld world world
                addEntity address entity_ world_)
            refWorld := changer !refWorld
            worldChangers.Add changer

    // TODO: add undo to quick size
    let handleQuickSize (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let optEntityTds = form.propertyGrid.SelectedObject
        match optEntityTds with
        | null -> ()
        | :? EntityTypeDescriptorSource as entityTds ->
            let changer = (fun world ->
                let entity_ = get world <| worldEntityLens entityTds.Address
                let entity_ = entity_.SetSize <| entity_.GetQuickSize world
                let world_ = set entity_ world <| worldEntityLens entityTds.Address
                refWorld := world_ // must be set for property grid
                form.propertyGrid.Refresh ()
                world_)
            refWorld := changer !refWorld
            worldChangers.Add changer
        | _ -> trace <| "Invalid quick size operation (likely a code issue in NuEdit)."

    let handleResetCamera (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let changer = (fun world ->
            let camera = { world.Camera with EyeCenter = Vector2.Zero }
            { world with Camera = camera })
        refWorld := changer !refWorld
        worldChangers.Add changer

    let handleAddXField (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        match (form.xFieldNameTextBox.Text, form.typeNameTextBox.Text) with
        | ("", _) -> ignore <| MessageBox.Show "Enter an XField name."
        | (_, "") -> ignore <| MessageBox.Show "Enter a type name."
        | (xFieldName, typeName) ->
            match tryFindType typeName with
            | None -> ignore <| MessageBox.Show "Enter a valid type name."
            | Some aType ->
                let selectedObject = form.propertyGrid.SelectedObject
                match selectedObject with
                | :? EntityTypeDescriptorSource as entityTds ->
                    let changer = (fun world ->
                        let entity = get world <| worldEntityLens entityTds.Address
                        let xFieldValue = if aType = typeof<string> then String.Empty :> obj else Activator.CreateInstance aType
                        let xFields = Map.add xFieldName xFieldValue entity.Xtension.XFields
                        let entity' = { entity with Xtension = { entity.Xtension with XFields = xFields }}
                        let world' = set entity' world <| worldEntityLens entityTds.Address
                        let world'' = pushPastWorld world world'
                        refWorld := world'' // must be set for property grid
                        form.propertyGrid.Refresh ()
                        form.propertyGrid.Select ()
                        form.propertyGrid.SelectedGridItem <- form.propertyGrid.SelectedGridItem.Parent.GridItems.[xFieldName]
                        world'')
                    refWorld := changer !refWorld
                    worldChangers.Add changer
                | _ -> ignore <| MessageBox.Show "Select an entity to add the XField to."

    let handleRemoveSelectedXField (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let selectedObject = form.propertyGrid.SelectedObject
        match selectedObject with
        | :? EntityTypeDescriptorSource as entityTds ->
            match form.propertyGrid.SelectedGridItem.Label with
            | "" -> ignore <| MessageBox.Show "Select an XField."
            | xFieldName ->
                let changer = (fun world ->
                    let entity = get world <| worldEntityLens entityTds.Address
                    let xFields = Map.remove xFieldName entity.Xtension.XFields
                    let entity' = { entity with Xtension = { entity.Xtension with XFields = xFields }}
                    let world' = set entity' world <| worldEntityLens entityTds.Address
                    let world'' = pushPastWorld world world'
                    refWorld := world'' // must be set for property grid
                    form.propertyGrid.Refresh ()
                    world'')
                refWorld := changer !refWorld
                worldChangers.Add changer
        | _ -> ignore <| MessageBox.Show "Select an entity to remove an XField from."

    let handleClearAllXFields (form : NuEditForm) (worldChangers : WorldChanger List) refWorld _ =
        let selectedObject = form.propertyGrid.SelectedObject
        match selectedObject with
        | :? EntityTypeDescriptorSource as entityTds ->
            let changer = (fun world ->
                let entity = get world <| worldEntityLens entityTds.Address
                let entity' = { entity with Xtension = { entity.Xtension with XFields = Map.empty }}
                let world' = set entity' world <| worldEntityLens entityTds.Address
                let world'' = pushPastWorld world world'
                refWorld := world'' // must be set for property grid
                form.propertyGrid.Refresh ()
                world'')
            refWorld := changer !refWorld
            worldChangers.Add changer
        | _ -> ignore <| MessageBox.Show "Select an entity to clear all XFields from."

    let createNuEditForm worldChangers refWorld =
        let form = new NuEditForm ()
        form.displayPanel.MaximumSize <- Drawing.Size (ResolutionX, ResolutionY)
        form.positionSnapTextBox.Text <- string DefaultPositionSnap
        form.rotationSnapTextBox.Text <- string DefaultRotationSnap
        form.creationDepthTextBox.Text <- string DefaultCreationDepth
        form.exitToolStripMenuItem.Click.Add (handleExit form)
        form.createEntityButton.Click.Add (handleCreate form worldChangers refWorld false)
        form.createToolStripMenuItem.Click.Add (handleCreate form worldChangers refWorld false)
        form.createContextMenuItem.Click.Add (handleCreate form worldChangers refWorld true)
        form.deleteEntityButton.Click.Add (handleDelete form worldChangers refWorld)
        form.deleteToolStripMenuItem.Click.Add (handleDelete form worldChangers refWorld)
        form.deleteContextMenuItem.Click.Add (handleDelete form worldChangers refWorld)
        form.saveToolStripMenuItem.Click.Add (handleSave form worldChangers refWorld)
        form.openToolStripMenuItem.Click.Add (handleOpen form worldChangers refWorld)
        form.undoButton.Click.Add (handleUndo form worldChangers refWorld)
        form.undoToolStripMenuItem.Click.Add (handleUndo form worldChangers refWorld)
        form.redoButton.Click.Add (handleRedo form worldChangers refWorld)
        form.redoToolStripMenuItem.Click.Add (handleRedo form worldChangers refWorld)
        form.interactButton.CheckedChanged.Add (handleInteractChanged form worldChangers refWorld)
        form.cutToolStripMenuItem.Click.Add (handleCut form worldChangers refWorld)
        form.cutContextMenuItem.Click.Add (handleCut form worldChangers refWorld)
        form.copyToolStripMenuItem.Click.Add (handleCopy form worldChangers refWorld)
        form.copyContextMenuItem.Click.Add (handleCopy form worldChangers refWorld)
        form.pasteToolStripMenuItem.Click.Add (handlePaste form worldChangers refWorld false)
        form.pasteContextMenuItem.Click.Add (handlePaste form worldChangers refWorld true)
        form.quickSizeToolStripButton.Click.Add (handleQuickSize form worldChangers refWorld)
        form.resetCameraButton.Click.Add (handleResetCamera form worldChangers refWorld)
        form.addXFieldButton.Click.Add (handleAddXField form worldChangers refWorld)
        form.removeSelectedXFieldButton.Click.Add (handleRemoveSelectedXField form worldChangers refWorld)
        populateEntityDispatcherComboBox form
        form.Show ()
        form

    let tryCreateEditorWorld form worldChangers refWorld sdlDeps =
        let screen = makeDissolveScreen 100 100
        let group = makeDefaultGroup ()
        let editorState =
            { DragEntityState = DragEntityNone
              DragCameraState = DragCameraNone
              OptGameDispatcherDescriptor = None
              PastWorlds = []
              FutureWorlds = []
              Clipboard = ref None }
        let gameDispatcher = GameDispatcher () :> obj
        let optWorld = tryCreateEmptyWorld sdlDeps gameDispatcher editorState
        match optWorld with
        | Left errorMsg -> Left errorMsg
        | Right world ->
            refWorld := world
            refWorld := addScreen EditorScreenAddress screen [(EditorGroupName, group, [])] !refWorld
            refWorld := set (Some EditorScreenAddress) !refWorld worldOptSelectedScreenAddressLens
            refWorld := subscribe DownMouseLeftEvent [] (CustomSub <| beginEntityDrag form worldChangers refWorld) !refWorld
            refWorld := subscribe UpMouseLeftEvent [] (CustomSub <| endEntityDrag form) !refWorld
            refWorld := subscribe DownMouseCenterEvent [] (CustomSub <| beginCameraDrag form worldChangers refWorld) !refWorld
            refWorld := subscribe UpMouseCenterEvent [] (CustomSub <| endCameraDrag form) !refWorld
            Right !refWorld

    // TODO: remove code duplication with below
    let updateUndo (form : NuEditForm) world =
        let editorState = world.ExtData :?> EditorState
        if form.undoToolStripMenuItem.Enabled then
            if List.isEmpty editorState.PastWorlds then
                form.undoButton.Enabled <- false
                form.undoToolStripMenuItem.Enabled <- false
        elif not <| List.isEmpty editorState.PastWorlds then
            form.undoButton.Enabled <- true
            form.undoToolStripMenuItem.Enabled <- true

    let updateRedo (form : NuEditForm) world =
        let editorState = world.ExtData :?> EditorState
        if form.redoToolStripMenuItem.Enabled then
            if List.isEmpty editorState.FutureWorlds then
                form.redoButton.Enabled <- false
                form.redoToolStripMenuItem.Enabled <- false
        elif not <| List.isEmpty editorState.FutureWorlds then
            form.redoButton.Enabled <- true
            form.redoToolStripMenuItem.Enabled <- true

    let updateEditorWorld form (worldChangers : WorldChangers) refWorld world =
        refWorld := updateEntityDrag form world
        refWorld := updateCameraDrag form !refWorld
        refWorld := Seq.fold (fun world' changer -> changer world') !refWorld worldChangers
        worldChangers.Clear ()
        let editorState = (!refWorld).ExtData :?> EditorState
        updateUndo form !refWorld
        updateRedo form !refWorld
        (not form.IsDisposed, !refWorld)

    let selectWorkingDirectory () =
        use openDialog = new OpenFileDialog ()
        openDialog.Filter <- "Executable Files (*.exe)|*.exe"
        openDialog.Title <- "Select your game's executable file to make its assets and XDispatchers available in the editor (or cancel for default assets)."
        if openDialog.ShowDialog () = DialogResult.OK then
            let workingDirectory = Path.GetDirectoryName openDialog.FileName
            Directory.SetCurrentDirectory workingDirectory

    let [<EntryPoint; STAThread>] main _ =
        initTypeConverters ()
        let worldChangers = WorldChangers ()
        let refWorld = ref Unchecked.defaultof<World>
        selectWorkingDirectory ()
        use form = createNuEditForm worldChangers refWorld
        let sdlViewConfig = ExistingWindow form.displayPanel.Handle
        let sdlRenderFlags = enum<SDL.SDL_RendererFlags> (int SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED ||| int SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC)
        let sdlConfig = makeSdlConfig sdlViewConfig form.displayPanel.MaximumSize.Width form.displayPanel.MaximumSize.Height sdlRenderFlags AudioBufferSizeDefault
        run4
            (tryCreateEditorWorld form worldChangers refWorld)
            (updateEditorWorld form worldChangers refWorld)
            (fun world -> form.displayPanel.Invalidate (); world)
            sdlConfig