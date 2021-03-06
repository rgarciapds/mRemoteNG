using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using mRemoteNG.App;
using mRemoteNG.Messages;


namespace mRemoteNG.Tools
{
	public class PortScanner
	{
		private readonly List<IPAddress> _ipAddresses = new List<IPAddress>();
		private readonly List<int> _ports = new List<int>();
		private Thread _scanThread;
		private readonly List<ScanHost> _scannedHosts = new List<ScanHost>();
				
        #region Public Methods
	
		public PortScanner(IPAddress ipAddress1, IPAddress ipAddress2, int port1, int port2)
		{
            var ipAddressStart = IpAddressMin(ipAddress1, ipAddress2);
            var ipAddressEnd = IpAddressMax(ipAddress1, ipAddress2);

            var portStart = Math.Min(port1, port2);
			var portEnd = Math.Max(port1, port2);
					
			_ports.Clear();
			for (var port = portStart; port <= portEnd; port++)
			{
				_ports.Add(port);
			}
            _ports.AddRange(new[] { ScanHost.SshPort, ScanHost.TelnetPort, ScanHost.HttpPort, ScanHost.HttpsPort, ScanHost.RloginPort, ScanHost.RdpPort, ScanHost.VncPort });

            _ipAddresses.Clear();
            _ipAddresses.AddRange(IpAddressArrayFromRange(ipAddressStart, ipAddressEnd));

            _scannedHosts.Clear();
        }
				
		public void StartScan()
		{
			_scanThread = new Thread(ScanAsync);
			_scanThread.SetApartmentState(ApartmentState.STA);
			_scanThread.IsBackground = true;
			_scanThread.Start();
		}
				
		public void StopScan()
		{
			_scanThread.Abort();
        }
				
		public static bool IsPortOpen(string hostname, string port)
		{
			try
			{
				var tcpClient = new TcpClient(hostname, Convert.ToInt32(port));
                tcpClient.Close(); 
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
        #endregion

        #region Private Methods

        private int _hostCount;
        private void ScanAsync()
		{
			try
			{
			    _hostCount = 0;
                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"Tools.PortScan: Starting scan of {_ipAddresses.Count} hosts...", true);
                foreach (var ipAddress in _ipAddresses)
				{
                    _beginHostScanEvent?.Invoke(ipAddress.ToString());

                    var pingSender = new Ping();

                    try
                    {
                        pingSender.PingCompleted += PingSender_PingCompleted;
                        pingSender.SendAsync(ipAddress, ipAddress);
                    }
                    catch (Exception ex)
                    {
                        Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"Tools.PortScan: Ping failed for {ipAddress} {Environment.NewLine} {ex.Message}", true);
                    }
                }
            }
			catch (Exception ex)
			{
				Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"StartScanBG failed (Tools.PortScan) {Environment.NewLine} {ex.Message}", true);
			}
		}

        /* Some examples found here:
         * http://stackoverflow.com/questions/2114266/convert-ping-application-to-multithreaded-version-to-increase-speed-c-sharp
         */
        private void PingSender_PingCompleted(object sender, PingCompletedEventArgs e)
        {
            // UserState is the IP Address
            var ip = e.UserState.ToString();
            var scanHost = new ScanHost(ip);
            _hostCount++;

            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"Tools.PortScan: Scanning {_hostCount} of {_ipAddresses.Count} hosts: {scanHost.HostIp}", true);

            if (e.Error != null)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"Ping failed to {e.UserState} {Environment.NewLine} {e.Error.Message}", true);
                scanHost.ClosedPorts.AddRange(_ports);
                scanHost.SetAllProtocols(false);
            }
            else if (e.Reply.Status == IPStatus.Success)
            {
                /* ping was successful, try to resolve the hostname */
                try
                {
                    scanHost.HostName = Dns.GetHostEntry(scanHost.HostIp).HostName;
                }
                catch (Exception dnsex)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                        $"Tools.PortScan: Could not resolve {scanHost.HostIp} {Environment.NewLine} {dnsex.Message}",
                        true);
                }

                if (string.IsNullOrEmpty(scanHost.HostName))
                {
                    scanHost.HostName = scanHost.HostIp;
                }

                foreach (var port in _ports)
                {
                    bool isPortOpen;
                    try
                    {
                        var tcpClient = new TcpClient(ip, port);
                        isPortOpen = true;
                        scanHost.OpenPorts.Add(port);
                        tcpClient.Close();
                    }
                    catch (Exception)
                    {
                        isPortOpen = false;
                        scanHost.ClosedPorts.Add(port);
                    }

                    if (port == ScanHost.SshPort)
                    {
                        scanHost.Ssh = isPortOpen;
                    }
                    else if (port == ScanHost.TelnetPort)
                    {
                        scanHost.Telnet = isPortOpen;
                    }
                    else if (port == ScanHost.HttpPort)
                    {
                        scanHost.Http = isPortOpen;
                    }
                    else if (port == ScanHost.HttpsPort)
                    {
                        scanHost.Https = isPortOpen;
                    }
                    else if (port == ScanHost.RloginPort)
                    {
                        scanHost.Rlogin = isPortOpen;
                    }
                    else if (port == ScanHost.RdpPort)
                    {
                        scanHost.Rdp = isPortOpen;
                    }
                    else if (port == ScanHost.VncPort)
                    {
                        scanHost.Vnc = isPortOpen;
                    }
                }
            }
            else if(e.Reply.Status != IPStatus.Success)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"Ping did not complete to {e.UserState} : {e.Reply.Status}", true);
                scanHost.ClosedPorts.AddRange(_ports);
                scanHost.SetAllProtocols(false);
            }

            // cleanup
            var p = (Ping)sender;
            p.PingCompleted -= PingSender_PingCompleted;
            p.Dispose();

            var h = string.IsNullOrEmpty(scanHost.HostName) ? "HostNameNotFound" : scanHost.HostName;
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"Tools.PortScan: Scan of {scanHost.HostIp} ({h}) complete.", true);

            _scannedHosts.Add(scanHost);
            _hostScannedEvent?.Invoke(scanHost, _hostCount, _ipAddresses.Count);

            if (_scannedHosts.Count == _ipAddresses.Count)
                _scanCompleteEvent?.Invoke(_scannedHosts);
        }
        
		private static IEnumerable<IPAddress> IpAddressArrayFromRange(IPAddress ipAddress1, IPAddress ipAddress2)
		{
			var startIpAddress = IpAddressMin(ipAddress1, ipAddress2);
			var endIpAddress = IpAddressMax(ipAddress1, ipAddress2);
					
			var startAddress = IpAddressToInt32(startIpAddress);
			var endAddress = IpAddressToInt32(endIpAddress);
			var addressCount = endAddress - startAddress;
					
			var addressArray = new IPAddress[addressCount + 1];
			var index = 0;
			for (var address = startAddress; address <= endAddress; address++)
			{
				addressArray[index] = IpAddressFromInt32(address);
				index++;
			}
					
			return addressArray;
		}
				
		private static IPAddress IpAddressMin(IPAddress ipAddress1, IPAddress ipAddress2)
		{
		    return IpAddressCompare(ipAddress1, ipAddress2) < 0 ? ipAddress1 : ipAddress2;
		}

	    private static IPAddress IpAddressMax(IPAddress ipAddress1, IPAddress ipAddress2)
	    {
	        return IpAddressCompare(ipAddress1, ipAddress2) > 0 ? ipAddress1 : ipAddress2;
	    }

	    private static int IpAddressCompare(IPAddress ipAddress1, IPAddress ipAddress2)
		{
			return IpAddressToInt32(ipAddress1) - IpAddressToInt32(ipAddress2);
		}
				
		private static int IpAddressToInt32(IPAddress ipAddress)
		{
			if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
			{
				throw (new ArgumentException("ipAddress"));
			}
					
			var addressBytes = ipAddress.GetAddressBytes(); // in network order (big-endian)
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(addressBytes); // to host order (little-endian)
			}
			Debug.Assert(addressBytes.Length == 4);
					
			return BitConverter.ToInt32(addressBytes, 0);
		}
				
		private static IPAddress IpAddressFromInt32(int ipAddress)
		{
			var addressBytes = BitConverter.GetBytes(ipAddress); // in host order
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(addressBytes); // to network order (big-endian)
			}
			Debug.Assert(addressBytes.Length == 4);

            return new IPAddress(addressBytes);
		}
        #endregion
				
        #region Events
		public delegate void BeginHostScanEventHandler(string host);
		private BeginHostScanEventHandler _beginHostScanEvent;
				
		public event BeginHostScanEventHandler BeginHostScan
		{
			add
			{
				_beginHostScanEvent = (BeginHostScanEventHandler) Delegate.Combine(_beginHostScanEvent, value);
			}
			remove
			{
				_beginHostScanEvent = (BeginHostScanEventHandler) Delegate.Remove(_beginHostScanEvent, value);
			}
		}
				
		public delegate void HostScannedEventHandler(ScanHost scanHost, int scannedHostCount, int totalHostCount);
		private HostScannedEventHandler _hostScannedEvent;
				
		public event HostScannedEventHandler HostScanned
		{
			add
			{
				_hostScannedEvent = (HostScannedEventHandler) Delegate.Combine(_hostScannedEvent, value);
			}
			remove
			{
				_hostScannedEvent = (HostScannedEventHandler) Delegate.Remove(_hostScannedEvent, value);
			}
		}
				
		public delegate void ScanCompleteEventHandler(List<ScanHost> hosts);
		private ScanCompleteEventHandler _scanCompleteEvent;
				
		public event ScanCompleteEventHandler ScanComplete
		{
			add
			{
				_scanCompleteEvent = (ScanCompleteEventHandler) Delegate.Combine(_scanCompleteEvent, value);
			}
			remove
			{
				_scanCompleteEvent = (ScanCompleteEventHandler) Delegate.Remove(_scanCompleteEvent, value);
			}
		}
        #endregion
	}
}