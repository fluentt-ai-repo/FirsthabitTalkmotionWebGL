mergeInto(LibraryManager.library, {

    // Helper: get the bridge object from parent window (since Unity runs in iframe)
    // Falls back to current window for standalone testing.
    $FH__postset: 'var _fhBridge = function() { try { if (window.parent && window.parent.FirsthabitBridge) return window.parent.FirsthabitBridge; } catch(e) {} return window.FirsthabitBridge || null; };',
    $FH: function() {},

    FH_OnBridgeReady__deps: ['$FH'],
    FH_OnBridgeReady: function() {
        console.log('[FirsthabitBridge] Bridge ready');
        var b = _fhBridge();
        if (b && b.onBridgeReady) b.onBridgeReady();
    },

    FH_OnPrepared__deps: ['$FH'],
    FH_OnPrepared: function(idPtr) {
        var id = UTF8ToString(idPtr);
        console.log('[FirsthabitBridge] Prepared: ' + id);
        var b = _fhBridge();
        if (b && b.onPrepared) b.onPrepared(id);
    },

    FH_OnPrepareFailed__deps: ['$FH'],
    FH_OnPrepareFailed: function(idPtr, errorPtr) {
        var id = UTF8ToString(idPtr);
        var error = UTF8ToString(errorPtr);
        console.log('[FirsthabitBridge] Prepare failed: ' + id + ' - ' + error);
        var b = _fhBridge();
        if (b && b.onPrepareFailed) b.onPrepareFailed(id, error);
    },

    FH_OnPlaybackStarted__deps: ['$FH'],
    FH_OnPlaybackStarted: function(idPtr) {
        var id = UTF8ToString(idPtr);
        console.log('[FirsthabitBridge] Playback started: ' + id);
        var b = _fhBridge();
        if (b && b.onPlaybackStarted) b.onPlaybackStarted(id);
    },

    FH_OnPlaybackCompleted__deps: ['$FH'],
    FH_OnPlaybackCompleted: function(idPtr) {
        var id = UTF8ToString(idPtr);
        console.log('[FirsthabitBridge] Playback completed: ' + id);
        var b = _fhBridge();
        if (b && b.onPlaybackCompleted) b.onPlaybackCompleted(id);
    },

    FH_OnSentenceStarted__deps: ['$FH'],
    FH_OnSentenceStarted: function(textPtr) {
        var text = UTF8ToString(textPtr);
        console.log('[FirsthabitBridge] Sentence started: ' + text);
        var b = _fhBridge();
        if (b && b.onSentenceStarted) b.onSentenceStarted(text);
    },

    FH_OnSentenceEnded__deps: ['$FH'],
    FH_OnSentenceEnded: function(textPtr) {
        var text = UTF8ToString(textPtr);
        console.log('[FirsthabitBridge] Sentence ended: ' + text);
        var b = _fhBridge();
        if (b && b.onSentenceEnded) b.onSentenceEnded(text);
    },

    FH_OnSubtitleStarted__deps: ['$FH'],
    FH_OnSubtitleStarted: function(textPtr) {
        var text = UTF8ToString(textPtr);
        console.log('[FirsthabitBridge] Subtitle started: ' + text);
        var b = _fhBridge();
        if (b && b.onSubtitleStarted) b.onSubtitleStarted(text);
    },

    FH_OnSubtitleEnded__deps: ['$FH'],
    FH_OnSubtitleEnded: function(textPtr) {
        var text = UTF8ToString(textPtr);
        console.log('[FirsthabitBridge] Subtitle ended: ' + text);
        var b = _fhBridge();
        if (b && b.onSubtitleEnded) b.onSubtitleEnded(text);
    },

    FH_OnRequestSent__deps: ['$FH'],
    FH_OnRequestSent: function(idPtr) {
        var id = UTF8ToString(idPtr);
        console.log('[FirsthabitBridge] Request sent: ' + id);
        var b = _fhBridge();
        if (b && b.onRequestSent) b.onRequestSent(id);
    },

    FH_OnResponseReceived__deps: ['$FH'],
    FH_OnResponseReceived: function(idPtr) {
        var id = UTF8ToString(idPtr);
        console.log('[FirsthabitBridge] Response received: ' + id);
        var b = _fhBridge();
        if (b && b.onResponseReceived) b.onResponseReceived(id);
    },

    FH_OnCacheInfo__deps: ['$FH'],
    FH_OnCacheInfo: function(jsonPtr) {
        var json = UTF8ToString(jsonPtr);
        console.log('[FirsthabitBridge] CacheInfo: ' + json);
        var b = _fhBridge();
        if (b && b.onCacheInfo) b.onCacheInfo(json);
    },

    FH_OnVolumeChanged__deps: ['$FH'],
    FH_OnVolumeChanged: function(volume) {
        console.log('[FirsthabitBridge] Volume changed: ' + volume);
        var b = _fhBridge();
        if (b && b.onVolumeChanged) b.onVolumeChanged(volume);
    },

    FH_OnError__deps: ['$FH'],
    FH_OnError: function(methodPtr, messagePtr) {
        var method = UTF8ToString(methodPtr);
        var message = UTF8ToString(messagePtr);
        console.error('[FirsthabitBridge] Error in ' + method + ': ' + message);
        var b = _fhBridge();
        if (b && b.onError) b.onError(method, message);
    },

    FH_CreateAudioBlobUrl: function(formatPtr) {
        var format = UTF8ToString(formatPtr);
        var base64 = "";
        try { base64 = window.parent._pendingAudioBase64 || ""; } catch(e) {}
        if (!base64) base64 = window._pendingAudioBase64 || "";

        // Clear after reading to free memory
        try { window.parent._pendingAudioBase64 = ""; } catch(e) {}
        window._pendingAudioBase64 = "";

        if (!base64) {
            var emptyBuf = _malloc(1);
            HEAPU8[emptyBuf] = 0;
            return emptyBuf;
        }

        var mimeType = "audio/wav";
        if (format === "mp3") mimeType = "audio/mpeg";
        else if (format === "ogg") mimeType = "audio/ogg";
        else if (format === "m4a") mimeType = "audio/mp4";

        var binaryStr = atob(base64);
        var bytes = new Uint8Array(binaryStr.length);
        for (var i = 0; i < binaryStr.length; i++) {
            bytes[i] = binaryStr.charCodeAt(i);
        }

        var blob = new Blob([bytes], { type: mimeType });
        var url = URL.createObjectURL(blob);

        var bufferSize = lengthBytesUTF8(url) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(url, buffer, bufferSize);
        return buffer;
    },

    FH_RevokeAudioBlobUrl: function(urlPtr) {
        var url = UTF8ToString(urlPtr);
        if (url) {
            try { URL.revokeObjectURL(url); } catch(e) {}
        }
    }

});
