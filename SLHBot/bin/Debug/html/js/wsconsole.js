function uuid() {
    var uuid = "", i, random;
    for (i = 0; i < 32; i++) {
        random = Math.random() * 16 | 0;
        if (i == 8 || i == 12 || i == 16 || i == 20)
            uuid += "-"
        uuid += (i == 12 ? 4 : (i == 16 ? (random & 3 | 8) : random)).toString(16);
    }
    return uuid;
}

class Standalone {
    //get Socket() { return this._Socket; }
    //get Remote() { return this._Remote; }
    get Client() { return this._Client; }

    constructor(ws_address) {
        var socket = new WebSocket(ws_address, "SLH-Message-Protocol-0001");
        var remote = new JRPC({ client: true });

        this._Socket = socket;
        this._Remote = remote;

        socket.onmessage = function (event) {
            remote.receive(event.data);
        };

        remote.setTransmitter(function (message, next) {
            try {
                socket.send(message);
                return next(false);
            } catch (e) {
                return next(true);
            }
        });
        this._Client = {
            Eval: function (expression, ...args) {
                var promise = new Promise(function (resolve, reject) {
                    remote.call("Client/Eval", [expression, args], function (error, result) { resolve(result); });
                });
                return promise;
            },
            EvalSet: function (expression, value) {
                var promise = new Promise(function (resolve, reject) {
                    remote.call("Client/Eval/Set", [expression, [value]], function (error, result) { resolve(result); });
                });
                return promise;
            },
            EvalAddEventHandler: function (expression, handler) {
                var handler_id = uuid();
                var promise = new Promise(function (resolve, reject) {
                    remote.call("Client/Eval/AddEventHandler", [expression, handler_id], function (error, result) { resolve(handler_id); });
                    var entry = {};
                    entry[handler_id] = function (params, next) {
                        try {
                            handler(...params);
                            next(false);
                        }
                        catch{
                            next(true);
                        }
                    };
                    remote.expose(entry);
                    remote.upgrade();
                });
                return promise;
            }
        };
    }

    Close() {
        this._Remote.close();
    }
}

var SLH = new Standalone("wss://localhost:5756");

function SendChatMessage() {
    var message = document.getElementById("ChatInput").value;
    SLH.Client.Eval("Say", message);
    return false;
}

function TeleportToAvatar() {
    var avatar_name = document.getElementById("AvatarNameInput").value;
    SLH.Client.Eval("OnTeleportToAvatar", avatar_name);
    return false;
}