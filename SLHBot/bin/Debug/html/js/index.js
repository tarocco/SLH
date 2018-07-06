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