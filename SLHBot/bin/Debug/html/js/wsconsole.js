var Socket = new WebSocket("wss://localhost:5756", "SLH-Message-Protocol-0001");
function SendChatMessage() {
    var msg = {
        Action: "Say",
        Message: document.getElementById("ChatInput").value
    };

    Socket.send(JSON.stringify(msg));
    document.getElementById("ChatInput").value = "";
    return false;
}
//function TeleportToAvatar() {
//    var msg = {
//        Action: "TeleportToAvatar",
//        AvatarName: document.getElementById("AvatarNameInput").value
//    };

//    Socket.send(JSON.stringify(msg));
//    return false;
//}

function GetAuthorizedAvatars() {
    return [...document.querySelectorAll(".AuthorizedAvatar")].map(f => f.value);
}

function OnApplyAuthorizedAvatars() {
    var authorized_avatars = GetAuthorizedAvatars();
    console.log(authorized_avatars);
}

Socket.onmessage = function (event) {

    var message = JSON.parse(event.data);

    if (message._event == "ViewerEffect") {
        // These are spammy!!!
        if (message.Type == 7)
            return;

        if (message.Type == 11) {
            // Selection
            var point = message.Position;
            //var simulator = message.Simulator;
            var simulator_handle = 0;// calculate from point
            var simulator = {
                "Handle": simulator_handle
                // No name!
            };
            GetObjectNearestPoint(simulator, point);
        }
    } else if (message._event == "GetObjectNearestPoint") {
        DebugObject(message.Simulator, message.Object);
    }

    var slh_console = document.querySelector("textarea#Console");

    // Round-trip
    var message_string = JSON.stringify(message);

    slh_console.textContent += message_string + "\n";
};

function GetObjectNearestPoint(simulator, point) {
    var data = {
        "Action": "GetObjectNearestPoint",
        //"Simulator": simulator,
        "Point": point
    };
    Socket.send(data);
}

function DebugObject(simulator, object) {
    var data = {
        "Action": "DebugObject",
        //"Simulator": simulator,
        "Object": object
    };
}