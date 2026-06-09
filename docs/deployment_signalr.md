# SignalR WebSockets Deployment & Diagnostics Guide

This document details the configuration required to support SignalR WebSockets in production environments (such as reverse proxies and Cloudflare) and explains how to run diagnostics.

---

## 1. Production Reverse Proxy Configuration

SignalR WebSockets require the reverse proxy to pass through the `Upgrade` and `Connection` HTTP headers. Without these, connection attempts will fail and fall back to Long Polling (or disconnect entirely).

### Nginx Configuration

Ensure the following headers are configured in your `location` block for the API server (especially for the `/hubs/` routes):

```nginx
location /hubs/ {
    proxy_pass http://localhost:5000;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_cache_bypass $http_upgrade;
    
    # Optional: Increase timeouts for active WebSockets connection
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
}
```

### Cloudflare WebSockets Support

- In the **Cloudflare Dashboard**, navigate to **Network**.
- Verify that **WebSockets** toggle is turned **On** (enabled by default).
- Ensure that the Cloudflare CDN doesn't interfere with chunked encoding/buffering for SignalR.

---

## 2. Testing Hub Connectivity

To verify if the SignalR hub is accessible and negotiating successfully, you can run a GET/POST request to the negotiation endpoint:

### Test URL:
`http://localhost:5000/hubs/slide-editor/negotiate` (Local)
`https://<your-production-domain>/hubs/slide-editor/negotiate` (Production)

### Expected Response Format (JSON):
```json
{
  "negotiateVersion": 1,
  "connectionId": "xyz-connection-id-here",
  "connectionToken": "token-hash-here",
  "availableTransports": [
    {
      "transport": "WebSockets",
      "transferFormats": ["Text", "Binary"]
    },
    {
      "transport": "ServerSentEvents",
      "transferFormats": ["Text"]
    },
    {
      "transport": "LongPolling",
      "transferFormats": ["Text", "Binary"]
    }
  ]
}
```
If you get a `404 Not Found` or `502 Bad Gateway`, check your API router path mappings and proxy setups.

---

## 3. Browser Console Diagnostics

When troubleshooting in the browser, check the browser console.

### Connection States:
- **`[SignalR] Starting connection...`**
  Logged when the client starts the connection attempt.
- **`[SignalR] Connected successfully. Joining deck: <deckId>`**
  Logged when WebSocket negotiation passes and connection joins a specific deck.
- **`[SignalR] Connection lost. Reconnecting...`** or **`[SignalR] Reconnecting due to error:`**
  Logged if the socket connection drops. The system is entering local-first offline fallback mode and queueing actions.
- **`[SignalR] Reconnected. Connection ID:`**
  Logged on successful automatic reconnect. Presence is resent and queued operations are flushed.

### Diagnostic Actions:
1. Open DevTools (F12) -> **Network** tab -> Filter by **WS** (WebSockets).
2. Look for `slide-editor?id=...` and inspect the frames (incoming/outgoing messages) to view live broadcasts.
3. If WebSockets are blocked, check if SignalR fell back to Long Polling (which is supported as a fallback transport in the client configuration).
