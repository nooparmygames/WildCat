// The browser half of WebGLWebSocketTransport (WebGLWebSocketTransport.cs next to this file).
// Each socket gets an integer id; the C# side registers four callbacks once and is called
// with that id when a socket opens, receives a binary message, reports an error or closes.
// Only binary messages are part of the WildCat protocol; text messages are dropped.
// Not verified in a browser yet.

var WildCatWebSocketLibrary = {
    $WildCatWS: {
        sockets: {},
        nextId: 1,
        onOpen: 0,
        onMessage: 0,
        onError: 0,
        onClose: 0
    },

    WildCatWebSocket_SetCallbacks: function (onOpen, onMessage, onError, onClose) {
        WildCatWS.onOpen = onOpen;
        WildCatWS.onMessage = onMessage;
        WildCatWS.onError = onError;
        WildCatWS.onClose = onClose;
    },

    WildCatWebSocket_Create: function (urlPointer) {
        var url = UTF8ToString(urlPointer);
        var id = WildCatWS.nextId++;
        var socket;
        try {
            socket = new WebSocket(url);
        } catch (e) {
            return -1;
        }
        socket.binaryType = 'arraybuffer';
        WildCatWS.sockets[id] = socket;

        socket.onopen = function () {
            {{{ makeDynCall('vi', 'WildCatWS.onOpen') }}}(id);
        };
        socket.onmessage = function (event) {
            if (!(event.data instanceof ArrayBuffer)) {
                return;
            }
            var bytes = new Uint8Array(event.data);
            var buffer = _malloc(bytes.length);
            HEAPU8.set(bytes, buffer);
            try {
                {{{ makeDynCall('viii', 'WildCatWS.onMessage') }}}(id, buffer, bytes.length);
            } finally {
                _free(buffer);
            }
        };
        socket.onerror = function () {
            {{{ makeDynCall('vi', 'WildCatWS.onError') }}}(id);
        };
        socket.onclose = function (event) {
            {{{ makeDynCall('vii', 'WildCatWS.onClose') }}}(id, event.code);
        };
        return id;
    },

    WildCatWebSocket_Send: function (id, bufferPointer, length) {
        var socket = WildCatWS.sockets[id];
        if (!socket || socket.readyState !== 1) {
            return 0;
        }
        // slice copies the bytes out of the heap, which may move before the browser sends them.
        socket.send(HEAPU8.slice(bufferPointer, bufferPointer + length));
        return 1;
    },

    WildCatWebSocket_Close: function (id, code) {
        var socket = WildCatWS.sockets[id];
        if (socket && (socket.readyState === 0 || socket.readyState === 1)) {
            socket.close(code);
        }
    },

    WildCatWebSocket_Free: function (id) {
        var socket = WildCatWS.sockets[id];
        if (!socket) {
            return;
        }
        socket.onopen = null;
        socket.onmessage = null;
        socket.onerror = null;
        socket.onclose = null;
        if (socket.readyState === 0 || socket.readyState === 1) {
            socket.close(1000);
        }
        delete WildCatWS.sockets[id];
    }
};

autoAddDeps(WildCatWebSocketLibrary, '$WildCatWS');
mergeInto(LibraryManager.library, WildCatWebSocketLibrary);
