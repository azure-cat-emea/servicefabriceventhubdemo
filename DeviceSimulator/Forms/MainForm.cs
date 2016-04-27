// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#region Using Directives



#endregion

namespace Microsoft.AzureCat.Samples.DeviceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Windows.Forms;
    using Microsoft.AzureCat.Samples.PayloadEntities;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public partial class MainForm : Form
    {
        #region Private Constants

        //***************************
        // Formats
        //***************************
        private const string DateFormat = "<{0,2:00}:{1,2:00}:{2,2:00}> {3}";
        private const string ExceptionFormat = "Exception: {0}";
        private const string InnerExceptionFormat = "InnerException: {0}";
        private const string LogFileNameFormat = "DeviceSimulatorLog-{0}.txt";
        private const string EventHubUrl = "https://{0}.servicebus.windows.net/{1}/publishers/{2}";

        //***************************
        // Constants
        //***************************
        private const string SaveAsTitle = "Save Log As";
        private const string SaveAsExtension = "txt";
        private const string SaveAsFilter = "Text Documents (*.txt)|*.txt";
        private const string Start = "Start";
        private const string Stop = "Stop";
        private const string SenderSharedAccessKey = "SenderSharedAccessKey";
        private const string DeviceId = "id";
        private const string DeviceName = "name";
        private const string DeviceStatus = "status";
        private const string Value = "value";
        private const string Timestamp = "timestamp";

        //***************************
        // Configuration Parameters
        //***************************
        private const string UrlParameter = "url";
        private const string NamespaceParameter = "namespace";
        private const string KeyNameParameter = "keyName";
        private const string KeyValueParameter = "keyValue";
        private const string EventHubParameter = "eventHub";
        private const string DeviceCountParameter = "deviceCount";
        private const string EventIntervalParameter = "eventInterval";
        private const string MinValueParameter = "minValue";
        private const string MaxValueParameter = "maxValue";
        private const string MinOffsetParameter = "minOffset";
        private const string MaxOffsetParameter = "maxOffset";
        private const string SpikePercentageParameter = "spikePercentage";
        private const string ApiVersion = "&api-version=2014-05";

        //***************************
        // Configuration Parameters
        //***************************
        private const string DefaultEventHubName = "DeviceDemoInputHub";
        private const string DefaultStatus = "Ok";
        private const int DefaultDeviceNumber = 10;
        private const int DefaultMinValue = 20;
        private const int DefaultMaxValue = 50;
        private const int DefaultMinOffset = 20;
        private const int DefaultMaxOffset = 50;
        private const int DefaultSpikePercentage = 10;
        private const int DefaultEventIntervalInMilliseconds = 100;


        //***************************
        // Messages
        //***************************
        private const string UrlCannotBeNull = "The device management service URL cannot be null.";
        private const string NamespaceCannotBeNull = "The Service Bus namespace cannot be null.";
        private const string EventHubNameCannotBeNull = "The Event Hub name cannot be null.";
        private const string KeyNameCannotBeNull = "The Key name cannot be null.";
        private const string KeyValueCannotBeNull = "The Key value cannot be null.";
        private const string EventHubCreatedOrRetrieved = "Event Hub [{0}] successfully retrieved.";
        private const string MessagingFactoryCreated = "Device[{0,3:000}]. MessagingFactory created.";
        private const string SasToken = "Device[{0,3:000}]. SAS Token created.";
        private const string EventHubClientCreated = "Device[{0,3:000}]. EventHubClient created: Path=[{1}].";
        private const string HttpClientCreated = "Device[{0,3:000}]. HttpClient created: BaseAddress=[{1}].";
        private const string SendFailed = "Device[{0,3:000}]. Message send failed: [{1}]";
        private const string EventHubDoesNotExists = "The Event Hub [{0}] does not exist.";
        private const string InitializingDevices = "Initializing devices...";
        private const string DevicesInitialized = "Devices initialized.";

        #endregion

        #region Private Fields

        private CancellationTokenSource cancellationTokenSource;
        private readonly Random random = new Random((int) DateTime.Now.Ticks);

        #endregion

        #region Public Constructor

        /// <summary>
        /// Initializes a new instance of the MainForm class.
        /// </summary>
        public MainForm()
        {
            this.InitializeComponent();
            this.ConfigureComponent();
            this.ReadConfiguration();
        }

        #endregion

        #region Public Methods

        public void ConfigureComponent()
        {
            this.txtNamespace.AutoSize = false;
            this.txtNamespace.Size = new Size(this.txtNamespace.Size.Width, 24);
            this.txtKeyName.AutoSize = false;
            this.txtKeyName.Size = new Size(this.txtKeyName.Size.Width, 24);
            this.txtKeyValue.AutoSize = false;
            this.txtKeyValue.Size = new Size(this.txtKeyValue.Size.Width, 24);
            this.cboEventHub.AutoSize = false;
            this.cboEventHub.Size = new Size(this.cboEventHub.Size.Width, 24);
            this.txtDeviceCount.AutoSize = false;
            this.txtDeviceCount.Size = new Size(this.txtDeviceCount.Size.Width, 24);
            this.txtEventIntervalInMilliseconds.AutoSize = false;
            this.txtEventIntervalInMilliseconds.Size = new Size(this.txtEventIntervalInMilliseconds.Size.Width, 24);
            this.txtMinValue.AutoSize = false;
            this.txtMinValue.Size = new Size(this.txtMinValue.Size.Width, 24);
            this.txtMaxValue.AutoSize = false;
            this.txtMaxValue.Size = new Size(this.txtMaxValue.Size.Width, 24);
            this.txtMinOffset.AutoSize = false;
            this.txtMinOffset.Size = new Size(this.txtMinOffset.Size.Width, 24);
            this.txtMaxOffset.AutoSize = false;
            this.txtMaxOffset.Size = new Size(this.txtMinOffset.Size.Width, 24);
        }

        public void HandleException(Exception ex)
        {
            if (string.IsNullOrEmpty(ex?.Message))
            {
                return;
            }
            this.WriteToLog(string.Format(CultureInfo.CurrentCulture, ExceptionFormat, ex.Message));
            if (!string.IsNullOrEmpty(ex.InnerException?.Message))
            {
                this.WriteToLog(string.Format(CultureInfo.CurrentCulture, InnerExceptionFormat, ex.InnerException.Message));
            }
        }

        #endregion

        #region Private Methods

        public static bool IsJson(string item)
        {
            if (item == null)
            {
                throw new ArgumentException("The item argument cannot be null.");
            }
            try
            {
                JToken obj = JToken.Parse(item);
                return obj != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string IndentJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            dynamic parsedJson = JsonConvert.DeserializeObject(json);
            return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
        }

        private void ReadConfiguration()
        {
            try
            {
                string urlValue = ConfigurationManager.AppSettings[UrlParameter] ?? DefaultEventHubName;
                string[] urls = urlValue.Split(',', ';');
                foreach (string url in urls)
                {
                    this.cboDeviceManagementServiceUrl.Items.Add(url);
                }
                this.cboDeviceManagementServiceUrl.SelectedIndex = 0;
                this.txtNamespace.Text = ConfigurationManager.AppSettings[NamespaceParameter];
                this.txtKeyName.Text = ConfigurationManager.AppSettings[KeyNameParameter];
                this.txtKeyValue.Text = ConfigurationManager.AppSettings[KeyValueParameter];
                string eventHubValue = ConfigurationManager.AppSettings[EventHubParameter] ?? DefaultEventHubName;
                string[] eventHubs = eventHubValue.Split(',', ';');
                foreach (string eventHub in eventHubs)
                {
                    this.cboEventHub.Items.Add(eventHub);
                }
                this.cboEventHub.SelectedIndex = 0;
                int value;
                string setting = ConfigurationManager.AppSettings[DeviceCountParameter];
                this.txtDeviceCount.Text = int.TryParse(setting, out value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : DefaultDeviceNumber.ToString(CultureInfo.InvariantCulture);
                setting = ConfigurationManager.AppSettings[EventIntervalParameter];
                this.txtEventIntervalInMilliseconds.Text = int.TryParse(setting, out value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : DefaultEventIntervalInMilliseconds.ToString(CultureInfo.InvariantCulture);
                setting = ConfigurationManager.AppSettings[MinValueParameter];
                this.txtMinValue.Text = int.TryParse(setting, out value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : DefaultMinValue.ToString(CultureInfo.InvariantCulture);
                setting = ConfigurationManager.AppSettings[MaxValueParameter];
                this.txtMaxValue.Text = int.TryParse(setting, out value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : DefaultMaxValue.ToString(CultureInfo.InvariantCulture);
                setting = ConfigurationManager.AppSettings[MinOffsetParameter];
                this.txtMinOffset.Text = int.TryParse(setting, out value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : DefaultMinOffset.ToString(CultureInfo.InvariantCulture);
                setting = ConfigurationManager.AppSettings[MaxOffsetParameter];
                this.txtMaxOffset.Text = int.TryParse(setting, out value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : DefaultMaxOffset.ToString(CultureInfo.InvariantCulture);
                setting = ConfigurationManager.AppSettings[SpikePercentageParameter];
                this.trackbarSpikePercentage.Value = int.TryParse(setting, out value)
                    ? value
                    : DefaultSpikePercentage;
            }
            catch (Exception ex)
            {
                this.HandleException(ex);
            }
        }

        private void WriteToLog(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(this.InternalWriteToLog), message);
            }
            else
            {
                this.InternalWriteToLog(message);
            }
        }

        private void InternalWriteToLog(string message)
        {
            lock (this)
            {
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }
                string[] lines = message.Split('\n');
                DateTime now = DateTime.Now;
                string space = new string(' ', 19);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (i == 0)
                    {
                        string line = string.Format(
                            DateFormat,
                            now.Hour,
                            now.Minute,
                            now.Second,
                            lines[i]);
                        this.lstLog.Items.Add(line);
                    }
                    else
                    {
                        this.lstLog.Items.Add(space + lines[i]);
                    }
                }
                this.lstLog.SelectedIndex = this.lstLog.Items.Count - 1;
                this.lstLog.SelectedIndex = -1;
            }
        }

        #endregion

        #region Event Handlers

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void clearLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.lstLog.Items.Clear();
        }

        /// <summary>
        /// Saves the log to a text file
        /// </summary>
        /// <param name="sender">MainForm object</param>
        /// <param name="e">System.EventArgs parameter</param>
        private void saveLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.lstLog.Items.Count <= 0)
                {
                    return;
                }
                this.saveFileDialog.Title = SaveAsTitle;
                this.saveFileDialog.DefaultExt = SaveAsExtension;
                this.saveFileDialog.Filter = SaveAsFilter;
                this.saveFileDialog.FileName = string.Format(
                    LogFileNameFormat,
                    DateTime.Now.ToString(CultureInfo.CurrentUICulture).Replace('/', '-').Replace(':', '-'));
                if (this.saveFileDialog.ShowDialog() != DialogResult.OK ||
                    string.IsNullOrEmpty(this.saveFileDialog.FileName))
                {
                    return;
                }
                using (StreamWriter writer = new StreamWriter(this.saveFileDialog.FileName))
                {
                    foreach (object t in this.lstLog.Items)
                    {
                        writer.WriteLine(t as string);
                    }
                }
            }
            catch (Exception ex)
            {
                this.HandleException(ex);
            }
        }

        private void logWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.splitContainer.Panel2Collapsed = !((ToolStripMenuItem) sender).Checked;
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutForm form = new AboutForm();
            form.ShowDialog();
        }

        private void lstLog_Leave(object sender, EventArgs e)
        {
            this.lstLog.SelectedIndex = -1;
        }

        private void button_MouseEnter(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control != null)
            {
                control.ForeColor = Color.White;
            }
        }

        private void button_MouseLeave(object sender, EventArgs e)
        {
            Control control = sender as Control;
            if (control != null)
            {
                control.ForeColor = SystemColors.ControlText;
            }
        }

        // ReSharper disable once FunctionComplexityOverflow
        private void MainForm_Paint(object sender, PaintEventArgs e)
        {
            int width = (this.mainHeaderPanel.Size.Width - 80)/2;
            int halfWidth = (width - 16)/2;
            int panelWidth = this.mainHeaderPanel.Size.Width - 32;
            int panelHeight = (this.mainHeaderPanel.Size.Height - 192)/2;

            this.txtNamespace.Size = new Size(width, this.txtNamespace.Size.Height);
            this.txtKeyName.Size = new Size(width, this.txtKeyName.Size.Height);
            this.txtKeyValue.Size = new Size(width, this.txtKeyValue.Size.Height);
            this.cboEventHub.Size = new Size(width, this.cboEventHub.Size.Height);
            this.txtMinOffset.Size = new Size(halfWidth, this.txtMinOffset.Size.Height);
            this.txtMaxOffset.Size = new Size(halfWidth, this.txtMaxOffset.Size.Height);
            this.txtDeviceCount.Size = new Size(halfWidth, this.txtDeviceCount.Size.Height);
            this.txtEventIntervalInMilliseconds.Size = new Size(halfWidth, this.txtEventIntervalInMilliseconds.Size.Height);
            this.txtMinValue.Size = new Size(halfWidth, this.txtMinValue.Size.Height);
            this.txtMaxValue.Size = new Size(halfWidth, this.txtMaxValue.Size.Height);
            this.trackbarSpikePercentage.Size = new Size(width, this.trackbarSpikePercentage.Size.Height);

            this.cboEventHub.Location = new Point(32 + width, this.cboEventHub.Location.Y);
            this.txtKeyValue.Location = new Point(32 + width, this.txtKeyValue.Location.Y);
            this.txtMaxValue.Location = new Point(32 + halfWidth, this.txtMaxValue.Location.Y);
            this.txtMinOffset.Location = new Point(32 + width, this.txtMaxOffset.Location.Y);
            this.txtMaxOffset.Location = new Point(48 + +width + halfWidth, this.txtMaxOffset.Location.Y);
            this.txtEventIntervalInMilliseconds.Location = new Point(32 + halfWidth, this.txtEventIntervalInMilliseconds.Location.Y);
            this.trackbarSpikePercentage.Location = new Point(32 + width, this.trackbarSpikePercentage.Location.Y);

            this.lblEventHub.Location = new Point(32 + width, this.lblEventHub.Location.Y);
            this.lblKeyValue.Location = new Point(32 + width, this.lblKeyValue.Location.Y);
            this.lblMaxValue.Location = new Point(32 + halfWidth, this.lblMaxValue.Location.Y);
            this.lblMinOffset.Location = new Point(32 + width, this.lblMaxOffset.Location.Y);
            this.lblMaxOffset.Location = new Point(48 + +width + halfWidth, this.lblMaxOffset.Location.Y);
            this.lblEventIntervalInMilliseconds.Location = new Point(32 + halfWidth, this.lblEventIntervalInMilliseconds.Location.Y);
            this.lblSpikePercentage.Location = new Point(32 + width, this.lblSpikePercentage.Location.Y);
            this.radioButtonHttps.Location = new Point(32 + halfWidth, this.radioButtonAmqp.Location.Y);

            this.grouperEventHub.Size = new Size(panelWidth, panelHeight);
            this.grouperDevice.Size = new Size(panelWidth, panelHeight);
            this.grouperDevice.Location = new Point(16, 136 + panelHeight);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            this.txtNamespace.SelectionLength = 0;
        }

        // ReSharper disable once FunctionComplexityOverflow
        private async void btnStart_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;

            try
            {
                if (string.Compare(this.btnStart.Text, Start, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // Validate parameters
                    if (!this.ValidateParameters())
                    {
                        return;
                    }

                    // Initialize Devices
                    if (this.chkInitializeDevices.Checked)
                    {
                        await this.InitializeDevicesAsync();
                        this.chkInitializeDevices.Checked = false;
                    }

                    // Start Devices
                    this.StartDevices();

                    // Change button text
                    this.btnStart.Text = Stop;
                }
                else
                {
                    // Stop Devices
                    this.StopDevices();

                    // Change button text
                    this.btnStart.Text = Start;
                }
            }
            catch (Exception ex)
            {
                this.HandleException(ex);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private bool ValidateParameters()
        {
            if (string.IsNullOrWhiteSpace(this.cboDeviceManagementServiceUrl.Text))
            {
                this.WriteToLog(UrlCannotBeNull);
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.txtNamespace.Text))
            {
                this.WriteToLog(NamespaceCannotBeNull);
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.cboEventHub.Text))
            {
                this.WriteToLog(EventHubNameCannotBeNull);
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.txtKeyName.Text))
            {
                this.WriteToLog(KeyNameCannotBeNull);
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.txtKeyValue.Text))
            {
                this.WriteToLog(KeyValueCannotBeNull);
                return false;
            }
            return true;
        }

        public static string CreateSasTokenForAmqpSender(
            string senderKeyName,
            string senderKey,
            string serviceNamespace,
            string hubName,
            string publisherName,
            TimeSpan tokenTimeToLive)
        {
            // This is the format of the publisher endpoint. Each device uses a different publisher endpoint.
            // sb://<NAMESPACE>.servicebus.windows.net/<EVENT_HUB_NAME>/publishers/<PUBLISHER_NAME>. 
            string serviceUri = ServiceBusEnvironment.CreateServiceUri(
                "sb",
                serviceNamespace,
                $"{hubName}/publishers/{publisherName}")
                .ToString()
                .Trim('/');
            // SharedAccessSignature sr=<URL-encoded-resourceURI>&sig=<URL-encoded-signature-string>&se=<expiry-time-in-ISO-8061-format. >&skn=<senderKeyName>
            return SharedAccessSignatureTokenProvider.GetSharedAccessSignature(senderKeyName, senderKey, serviceUri, tokenTimeToLive);
        }

        // Create a SAS token for a specified scope. SAS tokens are described in http://msdn.microsoft.com/en-us/library/windowsazure/dn170477.aspx.
        private static string CreateSasTokenForHttpsSender(
            string senderKeyName,
            string senderKey,
            string serviceNamespace,
            string hubName,
            string publisherName,
            TimeSpan tokenTimeToLive)
        {
            // Set token lifetime. When supplying a device with a token, you might want to use a longer expiration time.
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            TimeSpan difference = DateTime.Now.ToUniversalTime() - origin;
            long tokenExpirationTime = Convert.ToUInt32(difference.TotalSeconds) + tokenTimeToLive.Seconds;

            // https://<NAMESPACE>.servicebus.windows.net/<EVENT_HUB_NAME>/publishers/<PUBLISHER_NAME>. 
            string uri = ServiceBusEnvironment.CreateServiceUri(
                "https",
                serviceNamespace,
                $"{hubName}/publishers/{publisherName}")
                .ToString()
                .Trim('/');
            string stringToSign = HttpUtility.UrlEncode(uri) + "\n" + tokenExpirationTime;
            HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(senderKey));

            string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

            // SharedAccessSignature sr=<URL-encoded-resourceURI>&sig=<URL-encoded-signature-string>&se=<expiry-time-in-ISO-8061-format. >&skn=<senderKeyName>
            string token = String.Format(
                CultureInfo.InvariantCulture,
                "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
                HttpUtility.UrlEncode(uri),
                HttpUtility.UrlEncode(signature),
                tokenExpirationTime,
                senderKeyName);
            return token;
        }

        private int GetValue(
            int minValue,
            int maxValue,
            int minOffset,
            int maxOffset,
            int spikePercentage)
        {
            int value = this.random.Next(0, 100);
            if (value >= spikePercentage)
            {
                return this.random.Next(minValue, maxValue + 1);
            }
            int sign = this.random.Next(0, 2);
            int offset = this.random.Next(minOffset, maxOffset + 1);
            offset = sign == 0 ? -offset : offset;
            return this.random.Next(minValue, maxValue + 1) + offset;
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            this.lstLog.Items.Clear();
        }

        private async void StartDevices()
        {
            // Create namespace manager
            Uri namespaceUri = ServiceBusEnvironment.CreateServiceUri("sb", this.txtNamespace.Text, string.Empty);
            TokenProvider tokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(this.txtKeyName.Text, this.txtKeyValue.Text);
            NamespaceManager namespaceManager = new NamespaceManager(namespaceUri, tokenProvider);

            // Check if the event hub already exists, if not, create the event hub.
            if (!await namespaceManager.EventHubExistsAsync(this.cboEventHub.Text))
            {
                this.WriteToLog(string.Format(EventHubDoesNotExists, this.cboEventHub.Text));
                return;
            }
            EventHubDescription eventHubDescription = await namespaceManager.GetEventHubAsync(this.cboEventHub.Text);

            this.WriteToLog(string.Format(EventHubCreatedOrRetrieved, this.cboEventHub.Text));

            // Check if the SAS authorization rule used by devices to send events to the event hub already exists, if not, create the rule.
            SharedAccessAuthorizationRule authorizationRule = eventHubDescription.
                Authorization.
                FirstOrDefault(
                    r => string.Compare(
                        r.KeyName,
                        SenderSharedAccessKey,
                        StringComparison.InvariantCultureIgnoreCase)
                         == 0) as SharedAccessAuthorizationRule;

            if (authorizationRule == null)
            {
                authorizationRule = new SharedAccessAuthorizationRule(
                    SenderSharedAccessKey,
                    SharedAccessAuthorizationRule.GenerateRandomKey(),
                    new[]
                    {
                        AccessRights.Send
                    });
                eventHubDescription.Authorization.Add(authorizationRule);
                await namespaceManager.UpdateEventHubAsync(eventHubDescription);
            }

            this.cancellationTokenSource = new CancellationTokenSource();
            string serviceBusNamespace = this.txtNamespace.Text;
            string eventHubName = this.cboEventHub.Text;
            string senderKey = authorizationRule.PrimaryKey;
            string status = DefaultStatus;
            int eventInterval = this.txtEventIntervalInMilliseconds.IntegerValue;
            int minValue = this.txtMinValue.IntegerValue;
            int maxValue = this.txtMaxValue.IntegerValue;
            int minOffset = this.txtMinOffset.IntegerValue;
            int maxOffset = this.txtMaxOffset.IntegerValue;
            int spikePercentage = this.trackbarSpikePercentage.Value;
            CancellationToken cancellationToken = this.cancellationTokenSource.Token;

            // Create one task for each device
            for (int i = 1; i <= this.txtDeviceCount.IntegerValue; i++)
            {
                int deviceId = i;
#pragma warning disable 4014
#pragma warning disable 4014
                Task.Run(
                    async () =>
#pragma warning restore 4014
                    {
                        string deviceName = $"device{deviceId:000}";

                        if (this.radioButtonAmqp.Checked)
                        {
                            // The token has the following format: 
                            // SharedAccessSignature sr={URI}&sig={HMAC_SHA256_SIGNATURE}&se={EXPIRATION_TIME}&skn={KEY_NAME}
                            string token = CreateSasTokenForAmqpSender(
                                SenderSharedAccessKey,
                                senderKey,
                                serviceBusNamespace,
                                eventHubName,
                                deviceName,
                                TimeSpan.FromDays(1));
                            this.WriteToLog(string.Format(SasToken, deviceId));

                            MessagingFactory messagingFactory = MessagingFactory.Create(
                                ServiceBusEnvironment.CreateServiceUri("sb", serviceBusNamespace, ""),
                                new MessagingFactorySettings
                                {
                                    TokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(token),
                                    TransportType = TransportType.Amqp
                                });
                            this.WriteToLog(string.Format(MessagingFactoryCreated, deviceId));

                            // Each device uses a different publisher endpoint: [EventHub]/publishers/[PublisherName]
                            EventHubClient eventHubClient = messagingFactory.CreateEventHubClient($"{eventHubName}/publishers/{deviceName}");
                            this.WriteToLog(string.Format(EventHubClientCreated, deviceId, eventHubClient.Path));

                            while (!cancellationToken.IsCancellationRequested)
                            {
                                // Create random value
                                int value = this.GetValue(minValue, maxValue, minOffset, maxOffset, spikePercentage);
                                DateTime timestamp = DateTime.Now;

                                // Create EventData object with the payload serialized in JSON format 
                                Payload payload = new Payload
                                {
                                    DeviceId = deviceId,
                                    Name = deviceName,
                                    Status = status,
                                    Value = value,
                                    Timestamp = timestamp
                                };
                                string json = JsonConvert.SerializeObject(payload);
                                using (EventData eventData = new EventData(Encoding.UTF8.GetBytes(json))
                                {
                                    PartitionKey = deviceName
                                })
                                {
                                    // Create custom properties
                                    eventData.Properties.Add(DeviceId, deviceId);
                                    eventData.Properties.Add(DeviceName, deviceName);
                                    eventData.Properties.Add(DeviceStatus, status);
                                    eventData.Properties.Add(Value, value);
                                    eventData.Properties.Add(Timestamp, timestamp);

                                    // Send the event to the event hub
                                    await eventHubClient.SendAsync(eventData);
                                    this.WriteToLog(
                                        $"[Event] DeviceId=[{payload.DeviceId:000}] " +
                                        $"Value=[{payload.Value:000}] " +
                                        $"Timestamp=[{payload.Timestamp}]");
                                }

                                // Wait for the event time interval
                                Thread.Sleep(eventInterval);
                            }
                        }
                        else
                        {
                            // The token has the following format: 
                            // SharedAccessSignature sr={URI}&sig={HMAC_SHA256_SIGNATURE}&se={EXPIRATION_TIME}&skn={KEY_NAME}
                            string token = CreateSasTokenForHttpsSender(
                                SenderSharedAccessKey,
                                senderKey,
                                serviceBusNamespace,
                                eventHubName,
                                deviceName,
                                TimeSpan.FromDays(1));
                            this.WriteToLog(string.Format(SasToken, deviceId));

                            // Create HttpClient object used to send events to the event hub.
                            HttpClient httpClient = new HttpClient
                            {
                                BaseAddress =
                                    new Uri(
                                        string.Format(
                                            EventHubUrl,
                                            serviceBusNamespace,
                                            eventHubName,
                                            deviceName).ToLower())
                            };
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
                            httpClient.DefaultRequestHeaders.Add(
                                "ContentType",
                                "application/json;type=entry;charset=utf-8");
                            this.WriteToLog(string.Format(HttpClientCreated, deviceId, httpClient.BaseAddress));

                            while (!cancellationToken.IsCancellationRequested)
                            {
                                // Create random value
                                int value = this.GetValue(minValue, maxValue, minOffset, maxOffset, spikePercentage);
                                DateTime timestamp = DateTime.Now;

                                // Create EventData object with the payload serialized in JSON format 
                                Payload payload = new Payload
                                {
                                    DeviceId = deviceId,
                                    Name = deviceName,
                                    Status = status,
                                    Value = value,
                                    Timestamp = timestamp
                                };
                                string json = JsonConvert.SerializeObject(payload);

                                // Create HttpContent
                                ByteArrayContent postContent = new ByteArrayContent(Encoding.UTF8.GetBytes(json));

                                // Create custom properties
                                postContent.Headers.Add(DeviceId, deviceId.ToString(CultureInfo.InvariantCulture));
                                postContent.Headers.Add(DeviceName, deviceName);
                                //postContent.Headers.Add(DeviceStatus, location);
                                postContent.Headers.Add(Value, value.ToString(CultureInfo.InvariantCulture));
                                postContent.Headers.Add(Timestamp, timestamp.ToString(CultureInfo.InvariantCulture));

                                try
                                {
                                    HttpResponseMessage response =
                                        await
                                            httpClient.PostAsync(
                                                httpClient.BaseAddress + "/messages" + "?timeout=60" + ApiVersion,
                                                postContent,
                                                cancellationToken);
                                    response.EnsureSuccessStatusCode();
                                    this.WriteToLog(
                                        $"[Event] DeviceId=[{payload.DeviceId:000}] " +
                                        $"Value=[{payload.Value:000}] " +
                                        $"Timestamp=[{payload.Timestamp}]");
                                }
                                catch (HttpRequestException ex)
                                {
                                    this.WriteToLog(string.Format(SendFailed, deviceId, ex.Message));
                                }

                                // Wait for the event time interval
                                Thread.Sleep(eventInterval);
                            }
                        }
                    },
                    cancellationToken).ContinueWith(
                        t =>
#pragma warning restore 4014
#pragma warning restore 4014
                        {
                            if (t.IsFaulted && t.Exception != null)
                            {
                                this.HandleException(t.Exception);
                            }
                        },
                        cancellationToken);
            }
        }

        private void StopDevices()
        {
            this.cancellationTokenSource?.Cancel();
        }

        private async Task InitializeDevicesAsync()
        {
            Dictionary<string, List<Tuple<string, string>>> manufacturerDictionary = new Dictionary<string, List<Tuple<string, string>>>
            {
                {
                    "Contoso", new List<Tuple<string, string>>
                    {
                        new Tuple<string, string>("TS1", "Temperature Sensor"),
                        new Tuple<string, string>("TS2", "Temperature Sensor")
                    }
                }
                ,
                {
                    "Fabrikam", new List<Tuple<string, string>>
                    {
                        new Tuple<string, string>("HS1", "Humidity Sensor"),
                        new Tuple<string, string>("HS2", "Humidity Sensor")
                    }
                }
            };

            Dictionary<string, List<string>> siteDictionary = new Dictionary<string, List<string>>
            {
                {
                    "Italy",
                    new List<string> {"Milan", "Rome", "Turin"}
                },
                {
                    "Germany",
                    new List<string> {"Munich", "Berlin", "Amburg"}
                },
                {
                    "UK",
                    new List<string> {"London", "Manchester", "Liverpool"}
                },
                {
                    "France",
                    new List<string> {"Paris", "Lion", "Nice"}
                }
            };
            List<Device> deviceList = new List<Device>();

            // Prepare device data
            for (int i = 1; i <= this.txtDeviceCount.IntegerValue; i++)
            {
                int m = this.random.Next(0, manufacturerDictionary.Count);
                int d = this.random.Next(0, manufacturerDictionary.Values.ElementAt(m).Count);
                string model = manufacturerDictionary.Values.ElementAt(m)[d].Item1;
                string type = manufacturerDictionary.Values.ElementAt(m)[d].Item2;
                int s = this.random.Next(0, siteDictionary.Count);
                int c = this.random.Next(0, siteDictionary.Values.ElementAt(s).Count);

                deviceList.Add(
                    new Device
                    {
                        DeviceId = i,
                        Name = $"Device {i}",
                        MinThreshold = this.txtMinValue.IntegerValue,
                        MaxThreshold = this.txtMaxValue.IntegerValue,
                        Manufacturer = manufacturerDictionary.Keys.ElementAt(m),
                        Model = model,
                        Type = type,
                        City = siteDictionary.Values.ElementAt(s)[c],
                        Country = siteDictionary.Keys.ElementAt(s)
                    });
            }

            // Create HttpClient object used to send events to the event hub.
            HttpClient httpClient = new HttpClient
            {
                BaseAddress = new Uri(this.cboDeviceManagementServiceUrl.Text)
            };
            httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            string json = JsonConvert.SerializeObject(deviceList);

            // Create HttpContent
            StringContent postContent = new StringContent(json, Encoding.UTF8, "application/json");
            this.WriteToLog(InitializingDevices);
            HttpResponseMessage response = await httpClient.PostAsync(Combine(httpClient.BaseAddress.AbsoluteUri, "api/devices/set"), postContent);
            response.EnsureSuccessStatusCode();
            this.WriteToLog(DevicesInitialized);
        }

        public static string Combine(string uri1, string uri2)
        {
            uri1 = uri1.TrimEnd('/');
            uri2 = uri2.TrimStart('/');
            return $"{uri1}/{uri2}";
        }

        private void grouperDeviceManagement_CustomPaint(PaintEventArgs e)
        {
            e.Graphics.DrawRectangle(
                new Pen(SystemColors.ActiveBorder, 1),
                this.cboDeviceManagementServiceUrl.Location.X - 1,
                this.cboDeviceManagementServiceUrl.Location.Y - 1,
                this.cboDeviceManagementServiceUrl.Size.Width + 1,
                this.cboDeviceManagementServiceUrl.Size.Height + 1);
        }

        private void grouperEventHub_CustomPaint(PaintEventArgs e)
        {
            e.Graphics.DrawRectangle(
                new Pen(SystemColors.ActiveBorder, 1),
                this.cboEventHub.Location.X - 1,
                this.cboEventHub.Location.Y - 1,
                this.cboEventHub.Size.Width + 1,
                this.cboEventHub.Size.Height + 1);
        }

        #endregion
    }
}