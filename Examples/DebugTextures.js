(async() => {
    let slh_socket_address = "wss://" + window.location.hostname + ":5756";
    let slh_api_address = window.location.protocol + "//" + window.location.host;
    let slh_standalone_address = window.location.protocol + "//" + window.location.host;
    console.log(slh_api_address);
    let connection = await Standalone.ReopenNamedSocket(slh_socket_address, ["SLH-Message-Protocol-0001"], "Default");
    let client = new SLHClient(connection);
    let accept_teleport_lures_from = ["Philip Linden"]; // Add your avatar name to this list

    //// Handle Teleport Lures ////
    client.EvalAddEventHandler("Self.IM", (sender, args) => {
        let is_managing_avatar = accept_teleport_lures_from.includes(args.IM.FromAgentName);
        let is_teleport_lure = args.IM.Dialog == 22; // RequestTeleport
        if(is_managing_avatar && is_teleport_lure) 
            client.Eval("Self.TeleportLureRespond", args.IM.FromAgentID, args.IM.IMSessionID, true);
    });

    //// Handle Object Selections ////
    await client.EvalAddEventHandler("Avatars.ViewerEffectPointAt", async (sender, args) => {
        if(args.TargetID == NULL_KEY)
            return;
        let target_local_id = await client.Eval("GetPrimLocalId", args.TargetID);
        let target_prim = await client.Eval("Objects.GetPrimitive", args.Simulator, target_local_id, args.TargetID, false);
        let target_parent_id = target_prim.ParentID == 0 ? target_prim.LocalID : target_prim.ParentID;
        let target_link_set = await client.Eval("GetLinkSetLocalIds", target_parent_id);
        let target_prims_array = await Promise.all(target_link_set.map(id => client.Eval("Objects.GetPrimitive", args.Simulator, id, NULL_KEY, false)));
        let target_prims = Enumerable.from(target_prims_array);
        let default_textures = target_prims.select(prim => prim.Textures.DefaultTexture);
        let face_textures = target_prims.selectMany(prim => prim.Textures.FaceTextures);
        let all_textures = default_textures.concat(face_textures);
        let all_texture_ids = all_textures.where(t => t != null).select(t => t.TextureID);
        let target_textures = all_texture_ids.distinct();
        for(let texture of target_textures.toArray()) {
            let img = document.createElement("img");
            img.src = slh_api_address + "/texture/" + texture + ".png";
            img.style = "max-height: 128px; width: auto;";
            document.body.appendChild(img);
        }
    });

    //// Regular Updates ////
    let heading_angle = 0.0;
    async function update() {
        //console.log("heading == " + heading);
        let heading = [Math.cos(heading_angle), Math.sin(heading_angle), 0.0];
        await client.Eval("Self_Movement_LookDirection", heading, true);
        heading_angle += 0.1;
    }
    async function stats() {
        let tracking_size = await client.Eval("NumberOfTrackedObjects");
        console.log("Number of tracked objects: " + tracking_size);
    }
    let update_period = 50;
    let stats_period = 1000;
    let timer_wrapper = (handler, period) => {
        let closure = null;
        closure = async () => {
            if(connection.readyState === connection.OPEN) {
                await handler();
                setTimeout(closure, period);
            }
        };
        return closure;
    };
    timer_wrapper(update, update_period)();
    timer_wrapper(stats, stats_period)();
})()