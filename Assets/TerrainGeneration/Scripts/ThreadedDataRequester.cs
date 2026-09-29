using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;

public class ThreadedDataRequester : MonoBehaviour {

	static ThreadedDataRequester instance;
	Queue<ThreadInfo> dataQueue = new Queue<ThreadInfo>();

	void Awake() {
		instance = FindAnyObjectByType<ThreadedDataRequester> ();
	}

	public static void RequestData(Func<object> generateData, Action<object> callback) {
		ThreadStart threadStart = delegate {
			instance.DataThread (generateData, callback);
		};

		new Thread (threadStart).Start ();
	}

	void DataThread(Func<object> generateData, Action<object> callback) {
		try {
			object data = generateData ();
			lock (dataQueue) {
				dataQueue.Enqueue (new ThreadInfo (callback, data));
			}
		} catch (Exception e) {
			Debug.LogError ("ThreadedDataRequester: data generation failed, chunk will be missing: " + e);
		}
	}

	void Update() {
		// Worker threads enqueue while this runs, so empty the queue under the same lock.
		// Callbacks run outside the lock because they may request more data.
		ThreadInfo[] readyData;
		lock (dataQueue) {
			if (dataQueue.Count == 0) {
				return;
			}
			readyData = dataQueue.ToArray ();
			dataQueue.Clear ();
		}

		// Results have already left the queue, so a callback that throws must not stop the rest
		// (each lost result is a chunk or tile that never appears).
		foreach (ThreadInfo threadInfo in readyData) {
			try {
				threadInfo.callback (threadInfo.parameter);
			} catch (Exception e) {
				Debug.LogException (e);
			}
		}
	}

	struct ThreadInfo {
		public readonly Action<object> callback;
		public readonly object parameter;

		public ThreadInfo (Action<object> callback, object parameter)
		{
			this.callback = callback;
			this.parameter = parameter;
		}

	}
}
