﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Device = WinLocker.Models.Device;

namespace WinLocker;

public partial class MainPage : ContentPage
{
	/// <summary>
	/// Lock command string.
	/// </summary>
	private const string Lock = "LOCK";
	public readonly ObservableCollection<Device> Devices = [];
	private Device? SelectedDevice;

	public MainPage()
	{
		InitializeComponent();
		BindingContext = new Device();
	}

	private async void DeviceListView_Loaded(object sender, EventArgs e)
	{
		await ScanNetwork();
		DeviceListView.ItemsSource = Devices;
	}

	/// <summary>
	/// Unused for now.
	/// </summary>
	private static async Task<string> GetPublicIP()
	{
		try
		{
			using HttpClient client = new();
			string ip = await client.GetStringAsync("https://api.ipify.org");
			return ip;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
			return "Unable to get public IP.";
		}
	}

	private static async Task<string> GetDeviceName(string ip)
	{
		try
		{
			IPHostEntry hostEntry = await Dns.GetHostEntryAsync(ip);

			string deviceName = hostEntry.HostName;
			return deviceName;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error: {ex.Message}");
			return string.Empty;
		}
	}

	private void ToggleSpinner(bool toggle)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			LoadingSpinner.IsVisible = toggle;
			LoadingSpinner.IsRunning = toggle;
			DeviceListView.IsVisible = !toggle;
		});
	}

	private async Task ScanNetwork()
	{
		string privateIp = "192.168.1.1";
		string subnet = privateIp[..privateIp.LastIndexOf('.')];

		Console.WriteLine($"Scanning subnet: {subnet}.0/24");

		ToggleSpinner(true);

		List<Task> tasks = [];

		try
		{
			if (Devices != null && Devices.Any())
			{
				MainThread.BeginInvokeOnMainThread(Devices.Clear);
			}

			// Scan the subnet
			for (int i = 1; i < 255; i++)
			{
				string ip = $"{subnet}.{i}";

				tasks.Add(Task.Run(async () =>
				{
					using Ping ping = new();
					PingReply reply = await ping.SendPingAsync(ip, 100);

					if (reply.Status == IPStatus.Success)
					{
						Console.WriteLine($"Active device found: {ip}");

						Device device = new()
						{
							IPAddress = ip,
							DeviceName = await GetDeviceName(ip)
						};

						if (device.IPAddress != "192.168.1.1" && Devices != null)
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								Devices.Add(device);
							});
						}
					}
				}));
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error occured while scanning the network: " + ex.Message);
		}


		await Task.WhenAll(tasks);
		ToggleSpinner(false);
		Console.WriteLine("Finished scanning the subnet.");
	}

	private async void OnScanButtonClicked(object sender, EventArgs e)
	{
		await ScanNetwork();
		DeviceListView.ItemsSource = Devices;
	}

	private void DeviceListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
	{
		if (e.SelectedItem is Device device)
		{
			SelectedDevice = device;
		}
	}

	private async void OnLockButtonClicked(object sender, EventArgs e)
	{
		string password = PasswordEntry.Text;
		if (!string.IsNullOrEmpty(password))
		{
			string response = await SendLockCommand(password);
			ResponseLabel.Text = response;
		}
	}

	private async Task<string> SendLockCommand(string pwd)
	{
		string response = string.Empty;
		try
		{
			if (SelectedDevice != null)
			{
				using TcpClient client = new(SelectedDevice.IPAddress, 8080);

				client.ReceiveTimeout = 10000;

				using NetworkStream stream = client.GetStream();

				await SendCommand(stream, pwd);
				await ReadResponse(stream);
			}
			else throw new Exception("No device selected.");
		}
		catch (Exception ex)
		{
			response = $"Error: {ex.Message}";
		}

		return response;
	}

	private static async Task SendCommand(NetworkStream stream, string pwd)
	{
		string message = $"{Lock}:{pwd}";
		byte[] data = Encoding.UTF8.GetBytes(message);
		await stream.WriteAsync(data, 0, data.Length);
	}

	private static async Task<string> ReadResponse(NetworkStream stream)
	{
		try
		{
			byte[] buffer = new byte[1024];
			int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
			return Encoding.UTF8.GetString(buffer, 0, bytesRead);
		}
		catch (Exception ex)
		{
			return ex.Message;
		}
	}
}

