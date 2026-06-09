import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { getApiAuthToken, getApiOrigin } from '../../services/api';

function createClientId() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }

  return `slide-editor-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export default function useSlideEditorRealtime({ deckId, displayName, onOperation, onPresence, onSelection }) {
  const clientId = useMemo(createClientId, []);
  const connectionRef = useRef(null);
  const [status, setStatus] = useState('offline');
  const operationQueueRef = useRef([]);

  useEffect(() => {
    if (!deckId) {
      return undefined;
    }

    let disposed = false;

    const flushQueue = async (conn) => {
      if (operationQueueRef.current.length === 0) {
        return;
      }

      const queue = [...operationQueueRef.current];
      operationQueueRef.current = [];

      if (process.env.NODE_ENV === 'development') {
        console.log(`[SignalR] Flushing ${queue.length} queued operations.`);
      }

      for (const op of queue) {
        try {
          await conn.invoke('BroadcastOperation', op);
        } catch (err) {
          if (process.env.NODE_ENV === 'development') {
            console.error('[SignalR] Failed to send queued operation:', err);
          } else {
            console.warn('[SignalR] Failed to send queued operation.');
          }
          // Put back in queue if it fails again
          operationQueueRef.current.push(op);
        }
      }
    };

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${getApiOrigin()}/hubs/slide-editor`, {
        accessTokenFactory: () => getApiAuthToken(),
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Retry immediately, then 2s, 5s, 10s, 20s, and then 30s indefinitely
          const delays = [0, 2000, 5000, 10000, 20000];
          if (retryContext.previousRetryCount < delays.length) {
            return delays[retryContext.previousRetryCount];
          }
          return 30000;
        },
      })
      .build();

    connectionRef.current = connection;

    connection.on('SlideEditorOperation', (message) => {
      if (message?.clientId !== clientId) {
        onOperation?.(message);
      }
    });

    connection.on('SlideEditorSelection', (message) => {
      if (message?.clientId !== clientId) {
        onSelection?.(message);
      }
    });

    connection.on('SlideEditorPresence', (message) => {
      if (message?.clientId !== clientId) {
        onPresence?.(message);
      }
    });

    connection.onreconnecting((error) => {
      if (process.env.NODE_ENV === 'development') {
        console.log('[SignalR] Reconnecting due to error:', error);
      } else {
        console.warn('[SignalR] Connection lost. Reconnecting...');
      }
      setStatus('offline');
    });

    connection.onreconnected(async (connectionId) => {
      if (process.env.NODE_ENV === 'development') {
        console.log('[SignalR] Reconnected. Connection ID:', connectionId);
      }
      setStatus('connected');
      try {
        await connection.invoke('JoinDeck', deckId);
        await connection.invoke('BroadcastPresence', {
          deckId,
          clientId,
          displayName,
          status: 'online',
        });
        await flushQueue(connection);
      } catch (err) {
        if (process.env.NODE_ENV === 'development') {
          console.error('[SignalR] Post-reconnect join failed:', err);
        } else {
          console.warn('[SignalR] Post-reconnect join failed.');
        }
        setStatus('offline');
      }
    });

    connection.onclose((error) => {
      if (process.env.NODE_ENV === 'development') {
        console.log('[SignalR] Connection closed. Error:', error);
      } else {
        console.warn('[SignalR] Connection closed.');
      }
      setStatus('offline');
    });

    const start = async () => {
      try {
        if (process.env.NODE_ENV === 'development') {
          console.log('[SignalR] Starting connection...');
        }
        await connection.start();
        if (disposed) {
          return;
        }
        if (process.env.NODE_ENV === 'development') {
          console.log('[SignalR] Connected successfully. Joining deck:', deckId);
        }
        await connection.invoke('JoinDeck', deckId);
        await connection.invoke('BroadcastPresence', {
          deckId,
          clientId,
          displayName,
          status: 'online',
        });
        setStatus('connected');
        await flushQueue(connection);
      } catch (err) {
        if (process.env.NODE_ENV === 'development') {
          console.error('[SignalR] Connection start failed:', err);
        } else {
          console.warn('[SignalR] Connection start failed.');
        }
        setStatus('offline');
      }
    };

    start();

    return () => {
      disposed = true;
      connection.invoke('BroadcastPresence', {
        deckId,
        clientId,
        displayName,
        status: 'offline',
      }).catch(() => {});
      connection.invoke('LeaveDeck', deckId).catch(() => {});
      connection.stop().catch(() => {});
    };
  }, [clientId, deckId, displayName, onOperation, onPresence, onSelection]);

  const broadcastOperation = useCallback((message) => {
    const connection = connectionRef.current;
    const op = {
      deckId,
      clientId,
      operationId: createClientId(),
      ...message,
    };

    if (!connection || connection.state !== signalR.HubConnectionState.Connected || !deckId) {
      if (process.env.NODE_ENV === 'development') {
        console.log('[SignalR] Connection offline. Queueing operation:', op);
      }
      operationQueueRef.current.push(op);
      return false;
    }

    connection.invoke('BroadcastOperation', op).catch((err) => {
      if (process.env.NODE_ENV === 'development') {
        console.error('[SignalR] Invoke failed, queueing operation:', err);
      } else {
        console.warn('[SignalR] Invoke failed, queueing operation.');
      }
      operationQueueRef.current.push(op);
      setStatus('offline');
    });
    return true;
  }, [clientId, deckId]);

  const broadcastSelection = useCallback((message) => {
    const connection = connectionRef.current;
    if (!connection || connection.state !== signalR.HubConnectionState.Connected || !deckId) {
      return false;
    }

    connection.invoke('BroadcastSelection', {
      deckId,
      clientId,
      displayName,
      ...message,
    }).catch(() => setStatus('offline'));
    return true;
  }, [clientId, deckId, displayName]);

  return {
    broadcastOperation,
    broadcastSelection,
    clientId,
    status,
  };
}
