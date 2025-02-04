using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AnkrSDK.WalletConnectSharp.Core.Events;
using AnkrSDK.WalletConnectSharp.Core.Events.Model;
using AnkrSDK.WalletConnectSharp.Core.Models;
using AnkrSDK.WalletConnectSharp.Core.Network;
using AnkrSDK.WalletConnectSharp.Unity.Network.Client.Data;
using AnkrSDK.WalletConnectSharp.Unity.Network.Client.Exceptions;
using AnkrSDK.WalletConnectSharp.Unity.Network.Client.Implementation;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace AnkrSDK.WalletConnectSharp.Unity.Network
{
	public class NativeWebSocketTransport : MonoBehaviour, ITransport
	{
		private WebSocket _nextClient;
		private WebSocket _client;
		private EventDelegator _eventDelegator;
		private readonly List<string> _subscribedTopics = new List<string>();
		private readonly Queue<NetworkMessage> _queuedMessages = new Queue<NetworkMessage>();

		private bool _opened;		
		private bool _wasPaused;
		public bool Connected => _client?.State == WebSocketState.Open && _opened;

		public void AttachEventDelegator(EventDelegator eventDelegator)
		{
			_eventDelegator = eventDelegator;
		}

		public void Dispose()
		{
			_client?.CancelConnection();
		}

		public event EventHandler<MessageReceivedEventArgs> MessageReceived;
		public event EventHandler<MessageReceivedEventArgs> OpenReceived;
		public event EventHandler<MessageReceivedEventArgs> Closed;

		public string URL { get; private set; }

		public async Task Open(string url, bool clearSubscriptions = true)
		{
			if (URL != url || clearSubscriptions)
			{
				ClearSubscriptions();
			}

			URL = url;

			await _socketOpen();
		}

		private async Task _socketOpen()
		{
			Debug.Log("[WebSocket] Trying to open socket");
			if (_nextClient != null)
			{
				if (_nextClient.State == WebSocketState.Closed)
				{
					Debug.LogError("[WebSocket] Socket was closed but not cleared");
					_nextClient = null;
				}
				else
				{
					Debug.Log(
						$"[WebSocket] Will not try to open socket because it is already in state: {_nextClient.State}");
					return;
				}
			}

			var url = URL;
			if (url.StartsWith("https"))
			{
				url = url.Replace("https", "wss");
			}
			else if (url.StartsWith("http"))
			{
				url = url.Replace("http", "ws");
			}

			_nextClient = new WebSocket(url);

			var eventCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.None);

			_nextClient.OnOpen += () =>
			{
				CompleteOpen();

				// subscribe now
				OpenReceived?.Invoke(this, null);

				Debug.Log("[WebSocket] Opened " + url);

				eventCompleted.SetResult(true);
			};

			_nextClient.OnMessage += OnMessageReceived;
			_nextClient.OnClose += ClientTryReconnect;
			_nextClient.OnError += e =>
			{
				HandleError(new Exception(e));
			};

			StartClientConnect().Forget();

			Debug.Log("[WebSocket] Waiting for Open " + url);
			await eventCompleted.Task;
			Debug.Log("[WebSocket] Open Completed");
		}

		private async UniTaskVoid StartClientConnect()
		{
			var connectTask = _nextClient.Connect();
			await connectTask;
			if (connectTask.IsFaulted)
			{
				HandleError(connectTask.Exception);
			}
		}

		private void HandleError(Exception e)
		{
			Debug.LogError(e);
		}

		private async void CompleteOpen()
		{
			await Close();
			_client = _nextClient;
			_nextClient = null;
			QueueSubscriptions();
			_opened = true;
			FlushQueue();
		}

		private async void FlushQueue()
		{
			Debug.Log("[WebSocket] Flushing Queue. Count: " + _queuedMessages.Count);
			while (_queuedMessages.Count > 0)
			{
				var msg = _queuedMessages.Dequeue();
				await SendMessage(msg);
			}

			Debug.Log("[WebSocket] Queue Flushed");
		}

		private void QueueSubscriptions()
		{
			foreach (var topic in _subscribedTopics)
			{
				_queuedMessages.Enqueue(GenerateSubscribeMessage(topic));
			}

			Debug.Log("[WebSocket] Queued " + _subscribedTopics.Count + " subscriptions");
		}

		private async void ClientTryReconnect(WebSocketCloseCode closeCode)
		{
			if (_wasPaused)
			{
				Debug.Log("[WebSocket] Application paused, retry attempt aborted");
				return;
			}

			_nextClient = null;

			if (closeCode == WebSocketCloseCode.Abnormal)
			{
				const float reconnectDelay = 2f;

				Debug.LogError($"Abnormal close detected. Waiting for {reconnectDelay}s before reconnect");

				await UniTask.Delay(TimeSpan.FromSeconds(reconnectDelay));
			}

			await _socketOpen();
		}

		public void CancelConnection()
		{
			_client.CancelConnection();
		}

		private async void OnMessageReceived(byte[] bytes)
		{
			var json = System.Text.Encoding.UTF8.GetString(bytes);

			try
			{
				var msg = JsonConvert.DeserializeObject<NetworkMessage>(json);


				await SendMessage(new NetworkMessage()
				{
					Payload = "",
					Type = "ack",
					Silent = true,
					Topic = msg.Topic
				});

				MessageReceived?.Invoke(this, new MessageReceivedEventArgs(msg, this));
			}
			catch (Exception e)
			{
				Debug.Log("[WebSocket] Exception " + e.Message);
			}
		}

		private void Update()
		{
		#if !UNITY_WEBGL || UNITY_EDITOR
			if (_client?.State == WebSocketState.Open)
			{
				_client.DispatchMessageQueue();
			}
		#endif
		}

		public async Task Close()
		{
			Debug.Log("Closing Websocket");
			try
			{
				if (_client != null)
				{
					_opened = false;
					_client.OnClose -= ClientTryReconnect;
					await _client.Close();
				}
			}
			catch (WebSocketInvalidStateException e)
			{
				if (e.Message.Contains("WebSocket is not connected"))
				{
					Debug.LogWarning("Tried to close a websocket when it's already closed");
				}
				else
				{
					throw;
				}
			}
			finally
			{
				Closed?.Invoke(this, null);
			}
		}

		public async Task SendMessage(NetworkMessage message)
		{
			if (!Connected)
			{
				_queuedMessages.Enqueue(message);
				await _socketOpen();
			}
			else
			{
				var finalJson = JsonConvert.SerializeObject(message);

				await _client.SendText(finalJson);
			}
		}

		public async Task Subscribe(string topic)
		{
			Debug.Log("[WebSocket] Subscribe to " + topic);

			var msg = GenerateSubscribeMessage(topic);

			await SendMessage(msg);

			if (!_subscribedTopics.Contains(topic))
			{
				_subscribedTopics.Add(topic);
			}

			_opened = true;
		}

		private NetworkMessage GenerateSubscribeMessage(string topic)
		{
			return new NetworkMessage()
			{
				Payload = "",
				Type = "sub",
				Silent = true,
				Topic = topic
			};
		}

		public async Task Subscribe<T>(string topic, EventHandler<JsonRpcResponseEvent<T>> callback)
			where T : JsonRpcResponse
		{
			await Subscribe(topic);

			_eventDelegator.ListenFor(topic, callback);
		}

		public async Task Subscribe<T>(string topic, EventHandler<JsonRpcRequestEvent<T>> callback)
			where T : JsonRpcRequest
		{
			await Subscribe(topic);

			_eventDelegator.ListenFor(topic, callback);
		}

		public void ClearSubscriptions()
		{
			if (_eventDelegator != null)
			{
				foreach (var subscribedTopic in _subscribedTopics)
				{
					_eventDelegator.UnsubscribeProvider(subscribedTopic);
				}
			}

			_subscribedTopics.Clear();
			_queuedMessages.Clear();
		}

		private void OnApplicationPause(bool pauseStatus)
		{
			ProcessApplicationPause(pauseStatus).Forget();
		}

		private async UniTask ProcessApplicationPause(bool pauseStatus)
		{
			if (pauseStatus)
			{
				Debug.Log("[WebSocket] Pausing");
				_wasPaused = true;
				await Close();
			}
			else if (_wasPaused)
			{
				_wasPaused = false;
				Debug.Log("[WebSocket] Resuming");
				await Open(URL, false);

				foreach (var topic in _subscribedTopics)
				{
					await Subscribe(topic);
				}
			}
		}
	}
}