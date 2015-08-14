//#define DEBUG_MESSAGES

using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Bespoke.Common.Net;
using Bespoke.Common.Osc;

/// <summary>
/// OSC messenger for Unity.
/// Call SendCommand with OSC-compatible args.
/// Listen for messages by subscribing to onMessageReceived.
/// </summary>
public class Messenger : MonoBehaviour
{
	static Messenger Instance { get; set; }

	public int port = 9000;
	public string address = "255.255.255.255";
	public string oscRoot = "/oscmessenger";

	OscServer server;
	IPEndPoint receiveEndpoint;
	IPEndPoint sendEndpoint;

	Queue<OscMessage> producerQueue = new Queue<OscMessage>();
	Queue<OscMessage> consumerQueue = new Queue<OscMessage>();

	Queue<TimestampedMessage> recentMessages = new Queue<TimestampedMessage> ();

	public class Message
	{
		public string command;
		public IList<object> args;
	}

	public class MessengerEvent : UnityEvent<Message> {}

	public MessengerEvent onMessageReceived = new MessengerEvent ();


	void Awake ()
	{
		Instance = this;
	}

	void OnEnable ()
	{
		Initialize ();
		server.MessageReceived += HandleMessageReceived;
	}

	void OnDisable ()
	{
		server.MessageReceived -= HandleMessageReceived;
	}

	void Initialize ()
	{
		if (server == null)
		{
			server = new OscServer (IPAddress.Parse (address), port, TransmissionType.Broadcast);
			server.FilterRegisteredMethods = false;
			server.Start ();

			Debug.Log (string.Format ("[Messenger] initialized {0}:{1}", address, port));
		}
	}

	public static void SendCommand (string command, params object[] args)
	{
		Instance.DoSendCommand (command, args);
	}
	
	void DoSendCommand (string command, params object[] args)
	{
		var endPoint = new IPEndPoint(IPAddress.Parse(address), port);

		var oscMessage = new OscMessage (endPoint, "/" + command);

		foreach (var arg in args) {
			if (arg is int) {
				oscMessage.Append<int> ((int)arg);
			} else if (arg is float) {
				oscMessage.Append<float> ((float)arg);
			} else {
				oscMessage.Append<string> (arg.ToString());
			}
		}

		// Spam a few of the same message, effectively solves UDP unreliability in a clunky way
		for (int i = 0; i < 5; i++) {
			oscMessage.Send (endPoint);
		}
	}

	void Update()
	{
		if (producerQueue.Count > 0) {
			lock (producerQueue) {
				while (producerQueue.Count > 0) {
					consumerQueue.Enqueue (producerQueue.Dequeue());
				}
			}
			while (consumerQueue.Count > 0) {
				var message = consumerQueue.Dequeue();
				
				// Discard recent duplicate messages
				bool isSpam = false;
				foreach (var recentMessage in recentMessages) {
					bool isEqual = true;
					if (recentMessage.message.Address != message.Address) {
						isEqual = false;
					} else if (recentMessage.message.Data.Count != message.Data.Count) {
						isEqual = false;
					} else {
						for (int i = 0; i < message.Data.Count; i++) {
							if (!recentMessage.message.Data[i].Equals (message.Data[i])) {
								isEqual = false;
								break;
							}
						}
					}
					// Discard duplicate messages
					if (isEqual) {
//						#if DEBUG_MESSAGES
//						Debug.Log ("Discarded duplicate message: " + message.Address);
//						#endif
						if (Time.time - recentMessage.time < 0.3f) {
							isSpam = true;
						}
					}
				}
				if (isSpam) {
					continue;
				}
				
				// Record most recent message for later duplicate check
				if (recentMessages.Count > 30) {
					recentMessages.Dequeue ();
				}
				recentMessages.Enqueue (new TimestampedMessage (message, Time.time));
				
				var address = message.Address.Split('/') [1];
				
				#if DEBUG_MESSAGES
				string logMessage = message.Address;
				foreach (var d in message.Data)
				{
					logMessage += " " + d.ToString();
				}
				Debug.Log("[Messenger] Received: " + logMessage);
				#endif

				onMessageReceived.Invoke (new Message { command = address, args = message.Data });
			}
		}
	}

	void HandleMessageReceived (object sender, OscMessageReceivedEventArgs e)
	{
		lock (producerQueue) {
			producerQueue.Enqueue (e.Message);
		}
	}
	
	struct TimestampedMessage
	{
		public OscMessage message;
		public float time;
		
		public TimestampedMessage (OscMessage message, float time)
		{
			this.message = message;
			this.time = time;
		}
	}
}
