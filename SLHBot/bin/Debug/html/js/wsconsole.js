var Socket = new WebSocket("wss://localhost:5756", "SLH-Message-Protocol-0001");
var Remote = new JRPC({ client: true });

Remote.setTransmitter(function(message, next) {
	try {
        Socket.send(message);
        return next(false);
	} catch (e) {
        return next(true);
	}
});

function uuid()
{
    var uuid = "", i, random;
    for (i = 0; i < 32; i++)
    {
        random = Math.random() * 16 | 0;
        if (i == 8 || i == 12 || i == 16 || i == 20)
            uuid += "-"
        uuid += (i == 12 ? 4 : (i == 16 ? (random & 3 | 8) : random)).toString(16);
    }
    return uuid;
}

var Client = {
    Eval: function(expression, ...args)
    {
        var promise = new Promise(function(resolve, reject) {
            Remote.call("Client/Eval", [expression, args], function(error, result) { resolve(result); });
        });
        return promise;
    },
    EvalSet: function(expression, value)
    {
        var promise = new Promise(function(resolve, reject) {
            Remote.call("Client/Eval/Set", [expression, [value]], function(error, result) { resolve(result); });
        });
        return promise;
    },
    EvalAddEventHandler: function(expression, handler)
    {
        var handler_id = uuid();
        var promise = new Promise(function(resolve, reject) {
            Remote.call("Client/Eval/AddEventHandler", [expression, handler_id], function(error, result) { resolve(handler_id); });
            entry = {};
            entry[handler_id] = function(params, next) {
                try{
                    handler(...params);
                    next(false);
                }
                catch{
                    next(true);
                }
            };
            Remote.expose(entry);
            Remote.upgrade();
        });
        return promise;
    }
};

function SendChatMessage() {
    var message = document.getElementById("ChatInput").value;
    Remote.call("Client/Eval", ["OnSay", [message]]);
    return false;
}


function TeleportToAvatar() {
    var avatar_name = document.getElementById("AvatarNameInput").value;
    Remote.call("Client/Eval", ["OnTeleportToAvatar", [avatar_name]]);
    return false;
}

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
    
    Remote.receive(message_string);
    
    //slh_console.textContent += message_string + "\n";
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