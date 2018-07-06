const NULL_KEY = "00000000-0000-0000-0000-000000000000";

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

class StandaloneProperties {
    constructor() {
        this._Sockets = {};
    }
    async CloseNamedSocket(name) {
        return new Promise(async (resolve, reject) => {
            let socket = this._Sockets[name];
            if (socket instanceof WebSocket) {
                socket.onerror = (e) => {
                    socket.onerror = null;
                    socket.onopen = null;
                    reject(socket);
                };
                socket.onclose = (e) => {
                    socket.onerror = null;
                    socket.onopen = null;
                    resolve(socket);
                };
                socket.close();
            }
            else {
                resolve();
            }
        });
    }
    async ReopenNamedSocket(name, address) {
        if (typeof address === typeof undefined)
            address = name;
        return new Promise(async (resolve, reject) => {
            await this.CloseNamedSocket(name);
            let socket = new WebSocket(address);
            this._Sockets[name] = socket;
            socket.onerror = (e) => {
                socket.onerror = null;
                socket.onopen = null;
                reject(socket);
            };
            socket.onopen = (e) => {
                socket.onerror = null;
                socket.onopen = null;
                resolve(socket);
            };
        });
    }
}

let Standalone = new StandaloneProperties();

class SLHClient {
    constructor(socket) {
        let remote = new JRPC({ client: true });
        this._Remote = remote;
        this._Socket = socket;

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
    }
    Eval (expression, ...args) {
        let promise = new Promise((resolve, reject) => {
            this._Remote.call("Client/Eval", [expression, args], function (error, result) { resolve(result); });
        });
        return promise;
    }
    EvalSet (expression, value) {
        let promise = new Promise((resolve, reject) => {
            this._Remote.call("Client/Eval/Set", [expression, [value]], function (error, result) { resolve(result); });
        });
        return promise;
    }
    EvalAddEventHandler (expression, handler) {
        let handler_id = uuid();
        var promise = new Promise((resolve, reject) => {
            this._Remote.call("Client/Eval/AddEventHandler", [expression, handler_id], function (error, result) { resolve(handler_id); });
            let entry = {};
            entry[handler_id] = (params, next) => {
                try {
                    handler(...params);
                    next(false);
                }
                catch{
                    next(true);
                }
            };
            this._Remote.expose(entry);
            this._Remote.upgrade();
        });
        return promise;
    }
}