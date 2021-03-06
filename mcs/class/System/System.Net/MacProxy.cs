// 
// MacProxy.cs
//  
// Author: Jeffrey Stedfast <jeff@xamarin.com>
// 
// Copyright (c) 2012-2014 Xamarin Inc.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// 

using System;
using System.Net;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ObjCRuntimeInternal;

namespace Mono.Net
{
	internal class CFType {
		[DllImport (CFObject.CoreFoundationLibrary, EntryPoint="CFGetTypeID")]
		public static extern IntPtr GetTypeID (IntPtr typeRef);
	}

	internal class CFObject : IDisposable, INativeObject
	{
		public const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
		const string SystemLibrary = "/usr/lib/libSystem.dylib";

		[DllImport (SystemLibrary)]
		public static extern IntPtr dlopen (string path, int mode);

		[DllImport (SystemLibrary)]
		static extern IntPtr dlsym (IntPtr handle, string symbol);

		[DllImport (SystemLibrary)]
		public static extern void dlclose (IntPtr handle);

		public static IntPtr GetIndirect (IntPtr handle, string symbol)
		{
			return dlsym (handle, symbol);
		}

		public static CFString GetStringConstant (IntPtr handle, string symbol)
		{
			var indirect = dlsym (handle, symbol);
			if (indirect == IntPtr.Zero)
				return null;
			var actual = Marshal.ReadIntPtr (indirect);
			if (actual == IntPtr.Zero)
				return null;
			return new CFString (actual, false);
		}

		public static IntPtr GetIntPtr (IntPtr handle, string symbol)
		{
			var indirect = dlsym (handle, symbol);
			if (indirect == IntPtr.Zero)
				return IntPtr.Zero;
			return Marshal.ReadIntPtr (indirect);
		}

		public static IntPtr GetCFObjectHandle (IntPtr handle, string symbol)
		{
			var indirect = dlsym (handle, symbol);
			if (indirect == IntPtr.Zero)
				return IntPtr.Zero;

			return Marshal.ReadIntPtr (indirect);
		}

		public CFObject (IntPtr handle, bool own)
		{
			Handle = handle;

			if (!own)
				Retain ();
		}

		~CFObject ()
		{
			Dispose (false);
		}

		public IntPtr Handle { get; private set; }

		[DllImport (CoreFoundationLibrary)]
		internal extern static IntPtr CFRetain (IntPtr handle);

		void Retain ()
		{
			CFRetain (Handle);
		}

		[DllImport (CoreFoundationLibrary)]
		internal extern static void CFRelease (IntPtr handle);

		void Release ()
		{
			CFRelease (Handle);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (Handle != IntPtr.Zero) {
				Release ();
				Handle = IntPtr.Zero;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}
	}

	internal class CFArray : CFObject
	{
		public CFArray (IntPtr handle, bool own) : base (handle, own) { }

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFArrayCreate (IntPtr allocator, IntPtr values, /* CFIndex */ IntPtr numValues, IntPtr callbacks);
		static readonly IntPtr kCFTypeArrayCallbacks;

		static CFArray ()
		{
			var handle = dlopen (CoreFoundationLibrary, 0);
			if (handle == IntPtr.Zero)
				return;

			try {
				kCFTypeArrayCallbacks = GetIndirect (handle, "kCFTypeArrayCallBacks");
			} finally {
				dlclose (handle);
			}
		}
		
		public static CFArray FromNativeObjects (params INativeObject[] values)
		{
			return new CFArray (Create (values), true);
		}

		public static unsafe IntPtr Create (params IntPtr[] values)
		{
			if (values == null)
				throw new ArgumentNullException ("values");
			fixed (IntPtr* pv = values) {
				return CFArrayCreate (IntPtr.Zero, (IntPtr) pv, (IntPtr)values.Length, kCFTypeArrayCallbacks);
			}
		}

		internal static unsafe CFArray CreateArray (params IntPtr[] values)
		{
			if (values == null)
				throw new ArgumentNullException ("values");

			fixed (IntPtr *pv = values) {
				IntPtr handle = CFArrayCreate (IntPtr.Zero, (IntPtr) pv, (IntPtr) values.Length, kCFTypeArrayCallbacks);

				return new CFArray (handle, false);
			}
		}
		
		public static CFArray CreateArray (params INativeObject[] values)
		{
			return new CFArray (Create (values), true);
		}

		public static IntPtr Create (params INativeObject[] values)
		{
			if (values == null)
				throw new ArgumentNullException ("values");
			IntPtr[] _values = new IntPtr [values.Length];
			for (int i = 0; i < _values.Length; ++i)
				_values [i] = values [i].Handle;
			return Create (_values);
		}

		[DllImport (CoreFoundationLibrary)]
		extern static /* CFIndex */ IntPtr CFArrayGetCount (IntPtr handle);

		public int Count {
			get { return (int) CFArrayGetCount (Handle); }
		}

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFArrayGetValueAtIndex (IntPtr handle, /* CFIndex */ IntPtr index);

		public IntPtr this[int index] {
			get {
				return CFArrayGetValueAtIndex (Handle, (IntPtr) index);
			}
		}
		
		static public T [] ArrayFromHandle<T> (IntPtr handle, Func<IntPtr, T> creation) where T : class, INativeObject
		{
			if (handle == IntPtr.Zero)
				return null;

			var c = CFArrayGetCount (handle);
			T [] ret = new T [(int)c];

			for (uint i = 0; i < (uint)c; i++) {
				ret [i] = creation (CFArrayGetValueAtIndex (handle, (IntPtr)i));
			}
			return ret;
		}
	}

	internal class CFNumber : CFObject
	{
		public CFNumber (IntPtr handle, bool own) : base (handle, own) { }

		[DllImport (CoreFoundationLibrary)]
		[return: MarshalAs (UnmanagedType.I1)]
		extern static bool CFNumberGetValue (IntPtr handle, /* CFNumberType */ IntPtr type, [MarshalAs (UnmanagedType.I1)] out bool value);

		public static bool AsBool (IntPtr handle)
		{
			bool value;

			if (handle == IntPtr.Zero)
				return false;

			CFNumberGetValue (handle, (IntPtr) 1, out value);

			return value;
		}

		public static implicit operator bool (CFNumber number)
		{
			return AsBool (number.Handle);
		}

		[DllImport (CoreFoundationLibrary)]
		[return: MarshalAs (UnmanagedType.I1)]
		extern static bool CFNumberGetValue (IntPtr handle, /* CFNumberType */ IntPtr type, out int value);

		public static int AsInt32 (IntPtr handle)
		{
			int value;

			if (handle == IntPtr.Zero)
				return 0;

			// 9 == kCFNumberIntType == C int
			CFNumberGetValue (handle, (IntPtr) 9, out value);

			return value;
		}
		
		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFNumberCreate (IntPtr allocator, IntPtr theType, IntPtr valuePtr);	

		public static CFNumber FromInt32 (int number)
		{
			// 9 == kCFNumberIntType == C int
			return new CFNumber (CFNumberCreate (IntPtr.Zero, (IntPtr)9, (IntPtr)number), true);
		}

		public static implicit operator int (CFNumber number)
		{
			return AsInt32 (number.Handle);
		}
	}

	internal struct CFRange {
		public IntPtr Location, Length;
		
		public CFRange (int loc, int len)
		{
			Location = (IntPtr) loc;
			Length = (IntPtr) len;
		}
	}

	internal struct CFStreamClientContext {
		public IntPtr Version;
		public IntPtr Info;
		public IntPtr Retain;
		public IntPtr Release;
		public IntPtr CopyDescription;
	}

	internal class CFString : CFObject
	{
		string str;

		public CFString (IntPtr handle, bool own) : base (handle, own) { }

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFStringCreateWithCharacters (IntPtr alloc, IntPtr chars, /* CFIndex */ IntPtr length);

		public static CFString Create (string value)
		{
			IntPtr handle;

			unsafe {
				fixed (char *ptr = value) {
					handle = CFStringCreateWithCharacters (IntPtr.Zero, (IntPtr) ptr, (IntPtr) value.Length);
				}
			}

			if (handle == IntPtr.Zero)
				return null;

			return new CFString (handle, true);
		}

		[DllImport (CoreFoundationLibrary)]
		extern static /* CFIndex */ IntPtr CFStringGetLength (IntPtr handle);

		public int Length {
			get {
				if (str != null)
					return str.Length;

				return (int) CFStringGetLength (Handle);
			}
		}
		
		[DllImport (CoreFoundationLibrary)]
		extern static int CFStringCompare (IntPtr theString1, IntPtr theString2, int compareOptions);
		
		public static int Compare (IntPtr string1, IntPtr string2, int compareOptions = 0)
		{
			return CFStringCompare (string1, string2, compareOptions);
		}

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFStringGetCharactersPtr (IntPtr handle);

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFStringGetCharacters (IntPtr handle, CFRange range, IntPtr buffer);

		public static string AsString (IntPtr handle)
		{
			if (handle == IntPtr.Zero)
				return null;
			
			int len = (int) CFStringGetLength (handle);
			
			if (len == 0)
				return string.Empty;
			
			IntPtr chars = CFStringGetCharactersPtr (handle);
			IntPtr buffer = IntPtr.Zero;
			
			if (chars == IntPtr.Zero) {
				CFRange range = new CFRange (0, len);
				buffer = Marshal.AllocHGlobal (len * 2);
				CFStringGetCharacters (handle, range, buffer);
				chars = buffer;
			}

			string str;

			unsafe {
				str = new string ((char *) chars, 0, len);
			}
			
			if (buffer != IntPtr.Zero)
				Marshal.FreeHGlobal (buffer);

			return str;
		}

		public override string ToString ()
		{
			if (str == null)
				str = AsString (Handle);

			return str;
		}

		public static implicit operator string (CFString str)
		{
			return str.ToString ();
		}

		public static implicit operator CFString (string str)
		{
			return Create (str);
		}
	}

	
	internal class CFData : CFObject
	{
		public CFData (IntPtr handle, bool own) : base (handle, own) { }
	
		[DllImport (CoreFoundationLibrary)]
		extern static /* CFDataRef */ IntPtr CFDataCreate (/* CFAllocatorRef */ IntPtr allocator, /* UInt8* */ IntPtr bytes, /* CFIndex */ IntPtr length);
		public unsafe static CFData FromData (byte [] buffer)
		{
			fixed (byte* p = buffer)
			{
				return FromData ((IntPtr)p, (IntPtr)buffer.Length);
			}
		}

		public static CFData FromData (IntPtr buffer, IntPtr length)
		{
			return new CFData (CFDataCreate (IntPtr.Zero, buffer, length), true);
		}
		
		public IntPtr Length {
			get { return CFDataGetLength (Handle); }
		}

		[DllImport (CoreFoundationLibrary)]
		extern static /* CFIndex */ IntPtr CFDataGetLength (/* CFDataRef */ IntPtr theData);

		[DllImport (CoreFoundationLibrary)]
		extern static /* UInt8* */ IntPtr CFDataGetBytePtr (/* CFDataRef */ IntPtr theData);

		/*
		 * Exposes a read-only pointer to the underlying storage.
		 */
		public IntPtr Bytes {
			get { return CFDataGetBytePtr (Handle); }
		}

		public byte this [long idx] {
			get {
				if (idx < 0 || (ulong) idx > (ulong) Length)
					throw new ArgumentException ("idx");
				return Marshal.ReadByte (new IntPtr (Bytes.ToInt64 () + idx));
			}

			set {
				throw new NotImplementedException ("NSData arrays can not be modified, use an NSMutableData instead");
			}
		}

	}

	internal class CFDictionary : CFObject
	{
		static readonly IntPtr KeyCallbacks;
		static readonly IntPtr ValueCallbacks;
		
		static CFDictionary ()
		{
			var handle = dlopen (CoreFoundationLibrary, 0);
			if (handle == IntPtr.Zero)
				return;

			try {		
				KeyCallbacks = GetIndirect (handle, "kCFTypeDictionaryKeyCallBacks");
				ValueCallbacks = GetIndirect (handle, "kCFTypeDictionaryValueCallBacks");
			} finally {
				dlclose (handle);
			}
		}

		public CFDictionary (IntPtr handle, bool own) : base (handle, own) { }

		public static CFDictionary FromObjectAndKey (IntPtr obj, IntPtr key)
		{
			return new CFDictionary (CFDictionaryCreate (IntPtr.Zero, new IntPtr[] { key }, new IntPtr [] { obj }, (IntPtr)1, KeyCallbacks, ValueCallbacks), true);
		}

		public static CFDictionary FromKeysAndObjects (IList<Tuple<IntPtr,IntPtr>> items)
		{
			var keys = new IntPtr [items.Count];
			var values = new IntPtr [items.Count];
			for (int i = 0; i < items.Count; i++) {
				keys [i] = items [i].Item1;
				values [i] = items [i].Item2;
			}
			return new CFDictionary (CFDictionaryCreate (IntPtr.Zero, keys, values, (IntPtr)items.Count, KeyCallbacks, ValueCallbacks), true);
		}

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFDictionaryCreate (IntPtr allocator, IntPtr[] keys, IntPtr[] vals, IntPtr len, IntPtr keyCallbacks, IntPtr valCallbacks);

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFDictionaryGetValue (IntPtr handle, IntPtr key);

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFDictionaryCreateCopy (IntPtr allocator, IntPtr handle);

		public CFDictionary Copy ()
		{
			return new CFDictionary (CFDictionaryCreateCopy (IntPtr.Zero, Handle), true);
		}
		
		public CFMutableDictionary MutableCopy ()
		{
			return new CFMutableDictionary (CFDictionaryCreateMutableCopy (IntPtr.Zero, IntPtr.Zero, Handle), true);
		}

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFDictionaryCreateMutableCopy (IntPtr allocator, IntPtr capacity, IntPtr theDict);

		public IntPtr GetValue (IntPtr key)
		{
			return CFDictionaryGetValue (Handle, key);
		}

		public IntPtr this[IntPtr key] {
			get {
				return GetValue (key);
			}
		}
	}
	
	internal class CFMutableDictionary : CFDictionary
	{
		public CFMutableDictionary (IntPtr handle, bool own) : base (handle, own) { }

		public void SetValue (IntPtr key, IntPtr val)
		{
			CFDictionarySetValue (Handle, key, val);
		}

		public static CFMutableDictionary Create ()
		{
			var handle = CFDictionaryCreateMutable (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			if (handle == IntPtr.Zero)
				throw new InvalidOperationException ();
			return new CFMutableDictionary (handle, true);
		}

		[DllImport (CoreFoundationLibrary)]
		extern static void CFDictionarySetValue (IntPtr handle, IntPtr key, IntPtr val);

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFDictionaryCreateMutable (IntPtr allocator, IntPtr capacity, IntPtr keyCallback, IntPtr valueCallbacks);

	}

	internal class CFUrl : CFObject
	{
		public CFUrl (IntPtr handle, bool own) : base (handle, own) { }

		[DllImport (CoreFoundationLibrary)]
		extern static IntPtr CFURLCreateWithString (IntPtr allocator, IntPtr str, IntPtr baseURL);

		public static CFUrl Create (string absolute)
		{
			if (string.IsNullOrEmpty (absolute))
				return null;

			CFString str = CFString.Create (absolute);
			IntPtr handle = CFURLCreateWithString (IntPtr.Zero, str.Handle, IntPtr.Zero);
			str.Dispose ();

			if (handle == IntPtr.Zero)
				return null;

			return new CFUrl (handle, true);
		}
	}

	internal class CFRunLoop : CFObject
	{
		[DllImport (CFObject.CoreFoundationLibrary)]
		static extern void CFRunLoopAddSource (IntPtr rl, IntPtr source, IntPtr mode);

		[DllImport (CFObject.CoreFoundationLibrary)]
		static extern void CFRunLoopRemoveSource (IntPtr rl, IntPtr source, IntPtr mode);

		[DllImport (CFObject.CoreFoundationLibrary)]
		static extern int CFRunLoopRunInMode (IntPtr mode, double seconds, bool returnAfterSourceHandled);

		[DllImport (CFObject.CoreFoundationLibrary)]
		static extern IntPtr CFRunLoopGetCurrent ();

		[DllImport (CFObject.CoreFoundationLibrary)]
		static extern void CFRunLoopStop (IntPtr rl);

		public CFRunLoop (IntPtr handle, bool own): base (handle, own)
		{
		}

		public static CFRunLoop CurrentRunLoop {
			get { return new CFRunLoop (CFRunLoopGetCurrent (), false); }
		}

		public void AddSource (IntPtr source, CFString mode)
		{
			CFRunLoopAddSource (Handle, source, mode.Handle);
		}

		public void RemoveSource (IntPtr source, CFString mode)
		{
			CFRunLoopRemoveSource (Handle, source, mode.Handle);
		}

		public int RunInMode (CFString mode, double seconds, bool returnAfterSourceHandled)
		{
			return CFRunLoopRunInMode (mode.Handle, seconds, returnAfterSourceHandled);
		}

		public void Stop ()
		{
			CFRunLoopStop (Handle);
		}
	}

	internal enum CFProxyType {
		None,
		AutoConfigurationUrl,
		AutoConfigurationJavaScript,
		FTP,
		HTTP,
		HTTPS,
		SOCKS
	}
	
	internal class CFProxy {
		//static IntPtr kCFProxyAutoConfigurationHTTPResponseKey;
		static IntPtr kCFProxyAutoConfigurationJavaScriptKey;
		static IntPtr kCFProxyAutoConfigurationURLKey;
		static IntPtr kCFProxyHostNameKey;
		static IntPtr kCFProxyPasswordKey;
		static IntPtr kCFProxyPortNumberKey;
		static IntPtr kCFProxyTypeKey;
		static IntPtr kCFProxyUsernameKey;

		//static IntPtr kCFProxyTypeNone;
		static IntPtr kCFProxyTypeAutoConfigurationURL;
		static IntPtr kCFProxyTypeAutoConfigurationJavaScript;
		static IntPtr kCFProxyTypeFTP;
		static IntPtr kCFProxyTypeHTTP;
		static IntPtr kCFProxyTypeHTTPS;
		static IntPtr kCFProxyTypeSOCKS;

		static CFProxy ()
		{
			IntPtr handle = CFObject.dlopen (CFNetwork.CFNetworkLibrary, 0);

			//kCFProxyAutoConfigurationHTTPResponseKey = CFObject.GetCFObjectHandle (handle, "kCFProxyAutoConfigurationHTTPResponseKey");
			kCFProxyAutoConfigurationJavaScriptKey = CFObject.GetCFObjectHandle (handle, "kCFProxyAutoConfigurationJavaScriptKey");
			kCFProxyAutoConfigurationURLKey = CFObject.GetCFObjectHandle (handle, "kCFProxyAutoConfigurationURLKey");
			kCFProxyHostNameKey = CFObject.GetCFObjectHandle (handle, "kCFProxyHostNameKey");
			kCFProxyPasswordKey = CFObject.GetCFObjectHandle (handle, "kCFProxyPasswordKey");
			kCFProxyPortNumberKey = CFObject.GetCFObjectHandle (handle, "kCFProxyPortNumberKey");
			kCFProxyTypeKey = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeKey");
			kCFProxyUsernameKey = CFObject.GetCFObjectHandle (handle, "kCFProxyUsernameKey");

			//kCFProxyTypeNone = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeNone");
			kCFProxyTypeAutoConfigurationURL = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeAutoConfigurationURL");
			kCFProxyTypeAutoConfigurationJavaScript = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeAutoConfigurationJavaScript");
			kCFProxyTypeFTP = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeFTP");
			kCFProxyTypeHTTP = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeHTTP");
			kCFProxyTypeHTTPS = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeHTTPS");
			kCFProxyTypeSOCKS = CFObject.GetCFObjectHandle (handle, "kCFProxyTypeSOCKS");

			CFObject.dlclose (handle);
		}

		CFDictionary settings;
		
		internal CFProxy (CFDictionary settings)
		{
			this.settings = settings;
		}
		
		static CFProxyType CFProxyTypeToEnum (IntPtr type)
		{
			if (type == kCFProxyTypeAutoConfigurationJavaScript)
				return CFProxyType.AutoConfigurationJavaScript;

			if (type == kCFProxyTypeAutoConfigurationURL)
				return CFProxyType.AutoConfigurationUrl;

			if (type == kCFProxyTypeFTP)
				return CFProxyType.FTP;

			if (type == kCFProxyTypeHTTP)
				return CFProxyType.HTTP;

			if (type == kCFProxyTypeHTTPS)
				return CFProxyType.HTTPS;

			if (type == kCFProxyTypeSOCKS)
				return CFProxyType.SOCKS;
			
			//in OSX 10.13 pointer comparison didn't work for kCFProxyTypeAutoConfigurationURL
			if (CFString.Compare (type, kCFProxyTypeAutoConfigurationJavaScript) == 0)
				return CFProxyType.AutoConfigurationJavaScript;

			if (CFString.Compare (type, kCFProxyTypeAutoConfigurationURL) == 0)
				return CFProxyType.AutoConfigurationUrl;

			if (CFString.Compare (type, kCFProxyTypeFTP) == 0)
				return CFProxyType.FTP;

			if (CFString.Compare (type, kCFProxyTypeHTTP) == 0)
				return CFProxyType.HTTP;

			if (CFString.Compare (type, kCFProxyTypeHTTPS) == 0)
				return CFProxyType.HTTPS;

			if (CFString.Compare (type, kCFProxyTypeSOCKS) == 0)
				return CFProxyType.SOCKS;
			
			return CFProxyType.None;
		}
		
#if false
		// AFAICT these get used with CFNetworkExecuteProxyAutoConfiguration*()
		
		// TODO: bind CFHTTPMessage so we can return the proper type here.
		public IntPtr AutoConfigurationHTTPResponse {
			get { return settings[kCFProxyAutoConfigurationHTTPResponseKey]; }
		}
#endif

		public IntPtr AutoConfigurationJavaScript {
			get {
				if (kCFProxyAutoConfigurationJavaScriptKey == IntPtr.Zero)
					return IntPtr.Zero;
				
				return settings[kCFProxyAutoConfigurationJavaScriptKey];
			}
		}
		
		public IntPtr AutoConfigurationUrl {
			get {
				if (kCFProxyAutoConfigurationURLKey == IntPtr.Zero)
					return IntPtr.Zero;
				
				return settings[kCFProxyAutoConfigurationURLKey];
			}
		}
		
		public string HostName {
			get {
				if (kCFProxyHostNameKey == IntPtr.Zero)
					return null;
				
				return CFString.AsString (settings[kCFProxyHostNameKey]);
			}
		}
		
		public string Password {
			get {
				if (kCFProxyPasswordKey == IntPtr.Zero)
					return null;

				return CFString.AsString (settings[kCFProxyPasswordKey]);
			}
		}
		
		public int Port {
			get {
				if (kCFProxyPortNumberKey == IntPtr.Zero)
					return 0;
				
				return CFNumber.AsInt32 (settings[kCFProxyPortNumberKey]);
			}
		}
		
		public CFProxyType ProxyType {
			get {
				if (kCFProxyTypeKey == IntPtr.Zero)
					return CFProxyType.None;
				
				return CFProxyTypeToEnum (settings[kCFProxyTypeKey]);
			}
		}
		
		public string Username {
			get {
				if (kCFProxyUsernameKey == IntPtr.Zero)
					return null;

				return CFString.AsString (settings[kCFProxyUsernameKey]);
			}
		}
	}
	
	internal class CFProxySettings {
		static IntPtr kCFNetworkProxiesHTTPEnable;
		static IntPtr kCFNetworkProxiesHTTPPort;
		static IntPtr kCFNetworkProxiesHTTPProxy;
		static IntPtr kCFNetworkProxiesProxyAutoConfigEnable;
		static IntPtr kCFNetworkProxiesProxyAutoConfigJavaScript;
		static IntPtr kCFNetworkProxiesProxyAutoConfigURLString;

		static CFProxySettings ()
		{
			IntPtr handle = CFObject.dlopen (CFNetwork.CFNetworkLibrary, 0);

			kCFNetworkProxiesHTTPEnable = CFObject.GetCFObjectHandle (handle, "kCFNetworkProxiesHTTPEnable");
			kCFNetworkProxiesHTTPPort = CFObject.GetCFObjectHandle (handle, "kCFNetworkProxiesHTTPPort");
			kCFNetworkProxiesHTTPProxy = CFObject.GetCFObjectHandle (handle, "kCFNetworkProxiesHTTPProxy");
			kCFNetworkProxiesProxyAutoConfigEnable = CFObject.GetCFObjectHandle (handle, "kCFNetworkProxiesProxyAutoConfigEnable");
			kCFNetworkProxiesProxyAutoConfigJavaScript = CFObject.GetCFObjectHandle (handle, "kCFNetworkProxiesProxyAutoConfigJavaScript");
			kCFNetworkProxiesProxyAutoConfigURLString = CFObject.GetCFObjectHandle (handle, "kCFNetworkProxiesProxyAutoConfigURLString");

			CFObject.dlclose (handle);
		}

		CFDictionary settings;
		
		public CFProxySettings (CFDictionary settings)
		{
			this.settings = settings;
		}
		
		public CFDictionary Dictionary {
			get { return settings; }
		}
		
		public bool HTTPEnable {
			get {
				if (kCFNetworkProxiesHTTPEnable == IntPtr.Zero)
					return false;

				return CFNumber.AsBool (settings[kCFNetworkProxiesHTTPEnable]);
			}
		}
		
		public int HTTPPort {
			get {
				if (kCFNetworkProxiesHTTPPort == IntPtr.Zero)
					return 0;
				
				return CFNumber.AsInt32 (settings[kCFNetworkProxiesHTTPPort]);
			}
		}
		
		public string HTTPProxy {
			get {
				if (kCFNetworkProxiesHTTPProxy == IntPtr.Zero)
					return null;
				
				return CFString.AsString (settings[kCFNetworkProxiesHTTPProxy]);
			}
		}
		
		public bool ProxyAutoConfigEnable {
			get {
				if (kCFNetworkProxiesProxyAutoConfigEnable == IntPtr.Zero)
					return false;
				
				return CFNumber.AsBool (settings[kCFNetworkProxiesProxyAutoConfigEnable]);
			}
		}
		
		public string ProxyAutoConfigJavaScript {
			get {
				if (kCFNetworkProxiesProxyAutoConfigJavaScript == IntPtr.Zero)
					return null;
				
				return CFString.AsString (settings[kCFNetworkProxiesProxyAutoConfigJavaScript]);
			}
		}
		
		public string ProxyAutoConfigURLString {
			get {
				if (kCFNetworkProxiesProxyAutoConfigURLString == IntPtr.Zero)
					return null;
				
				return CFString.AsString (settings[kCFNetworkProxiesProxyAutoConfigURLString]);
			}
		}
	}
	
	internal static class CFNetwork {
#if !MONOTOUCH
		public const string CFNetworkLibrary = "/System/Library/Frameworks/CoreServices.framework/Frameworks/CFNetwork.framework/CFNetwork";
#else
		public const string CFNetworkLibrary = "/System/Library/Frameworks/CFNetwork.framework/CFNetwork";
#endif
		
		[DllImport (CFNetworkLibrary, EntryPoint = "CFNetworkCopyProxiesForAutoConfigurationScript")]
		// CFArrayRef CFNetworkCopyProxiesForAutoConfigurationScript (CFStringRef proxyAutoConfigurationScript, CFURLRef targetURL, CFErrorRef* error);
		extern static IntPtr CFNetworkCopyProxiesForAutoConfigurationScriptSequential (IntPtr proxyAutoConfigurationScript, IntPtr targetURL, out IntPtr error);

		[DllImport (CFNetworkLibrary)]
		extern static IntPtr CFNetworkExecuteProxyAutoConfigurationURL (IntPtr proxyAutoConfigURL, IntPtr targetURL, CFProxyAutoConfigurationResultCallback cb, ref CFStreamClientContext clientContext);


		class GetProxyData : IDisposable {
			public IntPtr script;
			public IntPtr targetUri;
			public IntPtr error;
			public IntPtr result;
			public ManualResetEvent evt = new ManualResetEvent (false);

			public void Dispose ()
			{
				evt.Close ();
			}
		}

		static object lock_obj = new object ();
		static Queue<GetProxyData> get_proxy_queue;
		static AutoResetEvent proxy_event;

		static void CFNetworkCopyProxiesForAutoConfigurationScriptThread ()
		{
			GetProxyData data;
			var data_left = true;

			while (true) {
				proxy_event.WaitOne ();

				do {
					lock (lock_obj) {
						if (get_proxy_queue.Count == 0)
							break;
						data = get_proxy_queue.Dequeue ();
						data_left = get_proxy_queue.Count > 0;
					}

					data.result = CFNetworkCopyProxiesForAutoConfigurationScriptSequential (data.script, data.targetUri, out data.error);
					data.evt.Set ();
				} while (data_left);
			}
		}

		static IntPtr CFNetworkCopyProxiesForAutoConfigurationScript (IntPtr proxyAutoConfigurationScript, IntPtr targetURL, out IntPtr error)
		{
			// This method must only be called on only one thread during an application's life time.
			// Note that it's not enough to use a lock to make calls sequential across different threads,
			// it has to be one thread. Also note that that thread can't be the main thread, because the
			// main thread might be blocking waiting for this network request to finish.
			// Another possibility would be to use JavaScriptCore to execute this piece of
			// javascript ourselves, but unfortunately it's not available before iOS7.
			// See bug #7923 comment #21+.

			using (var data = new GetProxyData ()) {
				data.script = proxyAutoConfigurationScript;
				data.targetUri = targetURL;

				lock (lock_obj) {
					if (get_proxy_queue == null) {
						get_proxy_queue = new Queue<GetProxyData> ();
						proxy_event = new AutoResetEvent (false);
						new Thread (CFNetworkCopyProxiesForAutoConfigurationScriptThread) {
							IsBackground = true,
						}.Start ();
					}
					get_proxy_queue.Enqueue (data);
					proxy_event.Set ();
				}

				data.evt.WaitOne ();

				error = data.error;

				return data.result;
			}
		}

		static CFArray CopyProxiesForAutoConfigurationScript (IntPtr proxyAutoConfigurationScript, CFUrl targetURL)
		{
			IntPtr err = IntPtr.Zero;
			IntPtr native = CFNetworkCopyProxiesForAutoConfigurationScript (proxyAutoConfigurationScript, targetURL.Handle, out err);
			
			if (native == IntPtr.Zero)
				return null;
			
			return new CFArray (native, true);
		}
		
		public static CFProxy[] GetProxiesForAutoConfigurationScript (IntPtr proxyAutoConfigurationScript, CFUrl targetURL)
		{
			if (proxyAutoConfigurationScript == IntPtr.Zero)
				throw new ArgumentNullException ("proxyAutoConfigurationScript");
			
			if (targetURL == null)
				throw new ArgumentNullException ("targetURL");
			
			CFArray array = CopyProxiesForAutoConfigurationScript (proxyAutoConfigurationScript, targetURL);
			
			if (array == null)
				return null;
			
			CFProxy[] proxies = new CFProxy [array.Count];
			for (int i = 0; i < proxies.Length; i++) {
				CFDictionary dict = new CFDictionary (array[i], false);
				proxies[i] = new CFProxy (dict);
			}

			array.Dispose ();
			
			return proxies;
		}
		
		public static CFProxy[] GetProxiesForAutoConfigurationScript (IntPtr proxyAutoConfigurationScript, Uri targetUri)
		{
			if (proxyAutoConfigurationScript == IntPtr.Zero)
				throw new ArgumentNullException ("proxyAutoConfigurationScript");
			
			if (targetUri == null)
				throw new ArgumentNullException ("targetUri");
			
			CFUrl targetURL = CFUrl.Create (targetUri.AbsoluteUri);
			CFProxy[] proxies = GetProxiesForAutoConfigurationScript (proxyAutoConfigurationScript, targetURL);
			targetURL.Dispose ();
			
			return proxies;
		}

		delegate void CFProxyAutoConfigurationResultCallback (IntPtr client, IntPtr proxyList, IntPtr error);

		public static CFProxy[] ExecuteProxyAutoConfigurationURL (IntPtr proxyAutoConfigURL, Uri targetURL)
		{
			CFUrl url = CFUrl.Create (targetURL.AbsoluteUri);
			if (url == null)
				return null;

			CFProxy[] proxies = null;

			var runLoop = CFRunLoop.CurrentRunLoop;

			// Callback that will be called after executing the configuration script
			CFProxyAutoConfigurationResultCallback cb = delegate (IntPtr client, IntPtr proxyList, IntPtr error) {
				if (proxyList != IntPtr.Zero) {
					var array = new CFArray (proxyList, false);
					proxies = new CFProxy [array.Count];
					for (int i = 0; i < proxies.Length; i++) {
						CFDictionary dict = new CFDictionary (array[i], false);
						proxies[i] = new CFProxy (dict);
					}
					array.Dispose ();
				}
				runLoop.Stop ();
			};

			var clientContext = new CFStreamClientContext ();
			var loopSource = CFNetworkExecuteProxyAutoConfigurationURL (proxyAutoConfigURL, url.Handle, cb, ref clientContext);

			// Create a private mode
			var mode = CFString.Create ("Mono.MacProxy");

			runLoop.AddSource (loopSource, mode);
			runLoop.RunInMode (mode, double.MaxValue, false);
			runLoop.RemoveSource (loopSource, mode);

			return proxies;
		}
		
		[DllImport (CFNetworkLibrary)]
		// CFArrayRef CFNetworkCopyProxiesForURL (CFURLRef url, CFDictionaryRef proxySettings);
		extern static IntPtr CFNetworkCopyProxiesForURL (IntPtr url, IntPtr proxySettings);
		
		static CFArray CopyProxiesForURL (CFUrl url, CFDictionary proxySettings)
		{
			IntPtr native = CFNetworkCopyProxiesForURL (url.Handle, proxySettings != null ? proxySettings.Handle : IntPtr.Zero);
			
			if (native == IntPtr.Zero)
				return null;
			
			return new CFArray (native, true);
		}
		
		public static CFProxy[] GetProxiesForURL (CFUrl url, CFProxySettings proxySettings)
		{
			if (url == null || url.Handle == IntPtr.Zero)
				throw new ArgumentNullException ("url");
			
			if (proxySettings == null)
				proxySettings = GetSystemProxySettings ();
			
			CFArray array = CopyProxiesForURL (url, proxySettings.Dictionary);
			
			if (array == null)
				return null;

			CFProxy[] proxies = new CFProxy [array.Count];
			for (int i = 0; i < proxies.Length; i++) {
				CFDictionary dict = new CFDictionary (array[i], false);
				proxies[i] = new CFProxy (dict);
			}

			array.Dispose ();
			
			return proxies;
		}
		
		public static CFProxy[] GetProxiesForUri (Uri uri, CFProxySettings proxySettings)
		{
			if (uri == null)
				throw new ArgumentNullException ("uri");
			
			CFUrl url = CFUrl.Create (uri.AbsoluteUri);
			if (url == null)
				return null;
			
			CFProxy[] proxies = GetProxiesForURL (url, proxySettings);
			url.Dispose ();
			
			return proxies;
		}
		
		[DllImport (CFNetworkLibrary)]
		// CFDictionaryRef CFNetworkCopySystemProxySettings (void);
		extern static IntPtr CFNetworkCopySystemProxySettings ();
		
		public static CFProxySettings GetSystemProxySettings ()
		{
			IntPtr native = CFNetworkCopySystemProxySettings ();
			
			if (native == IntPtr.Zero)
				return null;
			
			var dict = new CFDictionary (native, true);

			return new CFProxySettings (dict);
		}
		
		class CFWebProxy : IWebProxy {
			ICredentials credentials;
			bool userSpecified;
			
			public CFWebProxy ()
			{
			}
			
			public ICredentials Credentials {
				get { return credentials; }
				set {
					userSpecified = true;
					credentials = value;
				}
			}
			
			static Uri GetProxyUri (CFProxy proxy, out NetworkCredential credentials)
			{
				string protocol;
				
				switch (proxy.ProxyType) {
				case CFProxyType.FTP:
					protocol = "ftp://";
					break;
				case CFProxyType.HTTP:
				case CFProxyType.HTTPS:
					protocol = "http://";
					break;
				default:
					credentials = null;
					return null;
				}
				
				string username = proxy.Username;
				string password = proxy.Password;
				string hostname = proxy.HostName;
				int port = proxy.Port;
				string uri;
				
				if (username != null)
					credentials = new NetworkCredential (username, password);
				else
					credentials = null;
				
				uri = protocol + hostname + (port != 0 ? ':' + port.ToString () : string.Empty);
				
				return new Uri (uri, UriKind.Absolute);
			}
			
			static Uri GetProxyUriFromScript (IntPtr script, Uri targetUri, out NetworkCredential credentials)
			{
				CFProxy[] proxies = CFNetwork.GetProxiesForAutoConfigurationScript (script, targetUri);
				return SelectProxy (proxies, targetUri, out credentials);
			}

			static Uri ExecuteProxyAutoConfigurationURL (IntPtr proxyAutoConfigURL, Uri targetUri, out NetworkCredential credentials)
			{
				CFProxy[] proxies = CFNetwork.ExecuteProxyAutoConfigurationURL (proxyAutoConfigURL, targetUri);
				return SelectProxy (proxies, targetUri, out credentials);
			}


			static Uri SelectProxy (CFProxy[] proxies, Uri targetUri, out NetworkCredential credentials)
			{
				if (proxies == null) {
					credentials = null;
					return targetUri;
				}
				
				for (int i = 0; i < proxies.Length; i++) {
					switch (proxies[i].ProxyType) {
					case CFProxyType.HTTPS:
					case CFProxyType.HTTP:
					case CFProxyType.FTP:
						// create a Uri based on the hostname/port/etc info
						return GetProxyUri (proxies[i], out credentials);
					case CFProxyType.SOCKS:
					default:
						// unsupported proxy type, try the next one
						break;
					case CFProxyType.None:
						// no proxy should be used
						credentials = null;
						return targetUri;
					}
				}
				
				credentials = null;
				
				return null;
			}
			
			public Uri GetProxy (Uri targetUri)
			{
				NetworkCredential credentials = null;
				Uri proxy = null;
				
				if (targetUri == null)
					throw new ArgumentNullException ("targetUri");
				
				try {
					CFProxySettings settings = CFNetwork.GetSystemProxySettings ();
					CFProxy[] proxies = CFNetwork.GetProxiesForUri (targetUri, settings);
					
					if (proxies != null) {
						for (int i = 0; i < proxies.Length && proxy == null; i++) {
							switch (proxies[i].ProxyType) {
							case CFProxyType.AutoConfigurationJavaScript:
								proxy = GetProxyUriFromScript (proxies[i].AutoConfigurationJavaScript, targetUri, out credentials);
								break;
							case CFProxyType.AutoConfigurationUrl:
								proxy = ExecuteProxyAutoConfigurationURL (proxies[i].AutoConfigurationUrl, targetUri, out credentials);
								break;
							case CFProxyType.HTTPS:
							case CFProxyType.HTTP:
							case CFProxyType.FTP:
								// create a Uri based on the hostname/port/etc info
								proxy = GetProxyUri (proxies[i], out credentials);
								break;
							case CFProxyType.SOCKS:
								// unsupported proxy type, try the next one
								break;
							case CFProxyType.None:
								// no proxy should be used
								proxy = targetUri;
								break;
							}
						}
						
						if (proxy == null) {
							// no supported proxies for this Uri, fall back to trying to connect to targetUri directly
							proxy = targetUri;
						}
					} else {
						proxy = targetUri;
					}
				} catch {
					// ignore errors while retrieving proxy data
					proxy = targetUri;
				}
				
				if (!userSpecified)
					this.credentials = credentials;
				
				return proxy;
			}
			
			public bool IsBypassed (Uri targetUri)
			{
				if (targetUri == null)
					throw new ArgumentNullException ("targetUri");
				
				return GetProxy (targetUri) == targetUri;
			}
		}
		
		public static IWebProxy GetDefaultProxy ()
		{
			return new CFWebProxy ();
		}
	}

	class CFBoolean : INativeObject, IDisposable {
		IntPtr handle;

		public static readonly CFBoolean True;
		public static readonly CFBoolean False;

		static CFBoolean ()
		{
			var handle = CFObject.dlopen (CFObject.CoreFoundationLibrary, 0);
			if (handle == IntPtr.Zero)
				return;
			try {
				True  = new CFBoolean (CFObject.GetCFObjectHandle (handle, "kCFBooleanTrue"), false);
				False = new CFBoolean (CFObject.GetCFObjectHandle (handle, "kCFBooleanFalse"), false);
			}
			finally {
				CFObject.dlclose (handle);
			}
		}

		internal CFBoolean (IntPtr handle, bool owns)
		{
			this.handle = handle;
			if (!owns)
				CFObject.CFRetain (handle);
		}

		~CFBoolean ()
		{
			Dispose (false);
		}

		public IntPtr Handle {
			get {
				return handle;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (handle != IntPtr.Zero){
				CFObject.CFRelease (handle);
				handle = IntPtr.Zero;
			}
		}

		public static implicit operator bool (CFBoolean value)
		{
			return value.Value;
		}

		public static explicit operator CFBoolean (bool value)
		{
			return FromBoolean (value);
		}

		public static CFBoolean FromBoolean (bool value)
		{
			return value ? True : False;
		}

		[DllImport (CFObject.CoreFoundationLibrary)]
		[return: MarshalAs (UnmanagedType.I1)]
		extern static /* Boolean */ bool CFBooleanGetValue (/* CFBooleanRef */ IntPtr boolean);

		public bool Value {
			get {return CFBooleanGetValue (handle);}
		}

		public static bool GetValue (IntPtr boolean)
		{
			return CFBooleanGetValue (boolean);
		}
	}

	internal class CFDate : INativeObject, IDisposable {
		IntPtr handle;

		internal CFDate (IntPtr handle, bool owns)
		{
			this.handle = handle;
			if (!owns)
				CFObject.CFRetain (handle);
		}

		~CFDate ()
		{
			Dispose (false);
		}

		[DllImport (CFObject.CoreFoundationLibrary)]
		extern static IntPtr CFDateCreate (IntPtr allocator, /* CFAbsoluteTime */ double at);

		public static CFDate Create (DateTime date)
		{
			var referenceTime = new DateTime (2001, 1, 1);
			var difference = (date - referenceTime).TotalSeconds;
			var handle = CFDateCreate (IntPtr.Zero, difference);
			if (handle == IntPtr.Zero)
				throw new NotSupportedException ();
			return new CFDate (handle, true);
		}

		public IntPtr Handle {
			get {
				return handle;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (handle != IntPtr.Zero) {
				CFObject.CFRelease (handle);
				handle = IntPtr.Zero;
			}
		}

	}

}
