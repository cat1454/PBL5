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

  useEffect(() => {
    if (!deckId) {
      return undefined;
    }

    let disposed = false;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${getApiOrigin()}/hubs/slide-editor`, {
        accessTokenFactory: () => getApiAuthToken(),
      })
      .withAutomaticReconnect()
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

    connection.onreconnecting(() => setStatus('offline'));
    connection.onreconnected(async () => {
      setStatus('connected');
      try {
        await connection.invoke('JoinDeck', deckId);
      } catch {
        setStatus('offline');
      }
    });
    connection.onclose(() => setStatus('offline'));

    const start = async () => {
      try {
        await connection.start();
        if (disposed) {
          return;
        }
        await connection.invoke('JoinDeck', deckId);
        await connection.invoke('BroadcastPresence', {
          deckId,
          clientId,
          displayName,
          status: 'online',
        });
        setStatus('connected');
      } catch {
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
    if (!connection || connection.state !== signalR.HubConnectionState.Connected || !deckId) {
      return false;
    }

    connection.invoke('BroadcastOperation', {
      deckId,
      clientId,
      operationId: createClientId(),
      ...message,
    }).catch(() => setStatus('offline'));
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
