using Newtonsoft.Json;
using Quartz;
using Quartz.Impl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CatSniper
{
    public class MinecraftNameStatus
    {
        public string Status { get; set; } // AVAILABLE, DUPLICATE
    }

    public class HelloJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            if (!context.CancellationToken.IsCancellationRequested)
            {
                MainWindow.Instance.Dispatcher.Invoke(new Action(() =>
                {
                    MainWindow.Instance.AddText("Trying to change Minecraft Name...");
                }));

                JobKey key = context.JobDetail.Key;

                JobDataMap dataMap = context.JobDetail.JobDataMap;

                string authorization = dataMap.GetString("authorization")!;
                string minecraftName = dataMap.GetString("minecraftName")!;

                HttpClient client = new HttpClient();

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", authorization);

                var url = $"https://api.minecraftservices.com/minecraft/profile/name/{minecraftName}";

                for (var intent = 0; intent < 2; intent++)
                {
                    var date = DateTime.Now.ToString("HH:mm:ss.fff");
                    MainWindow.Instance.Dispatcher.Invoke(new Action(() =>
                    {
                        MainWindow.Instance.AddText($"Trying to change Minecraft at {date}");
                    }));

                    HttpResponseMessage? response = await client.PutAsync(url, null);

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        MainWindow.Instance.Dispatcher.Invoke(new Action(() =>
                        {
                            MainWindow.Instance.AddText("Minecraft Name changed successfully!");
                        }));
                        break;
                    }
                    else
                    {
                        MainWindow.Instance.Dispatcher.Invoke(new Action(() =>
                        {
                            MainWindow.Instance.AddText($"Unable to change Minecraft Name. Status code: " + response.StatusCode);
                        }));
                    }
                }
                MainWindow.Instance.ChangeState(true);
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        CancellationTokenSource cancellationToken;
        IScheduler scheduler;
        IJobDetail job;

        public static MainWindow Instance { get; private set; }

        public MainWindow()
        {
            Instance = this;

            InitializeComponent();

            ExpiryDate.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        }

        private void Request_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Authorization.Text))
            {
                MessageBox.Show("Bearer Token is required", "CatSniper",MessageBoxButton.OK,MessageBoxImage.Exclamation);
                return;
            }
            if (string.IsNullOrWhiteSpace(MinecraftName.Text))
            {
                MessageBox.Show("Minecraft name is required", "CatSniper", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (string.IsNullOrWhiteSpace(ExpiryDate.Text.Trim()))
            {
                MessageBox.Show("Time of Availability is required", "CatSniper", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!DateTime.TryParse(ExpiryDate.Text, out DateTime dateTime))
            {
                MessageBox.Show("Invalid Time of Availability value", "CatSniper", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (dateTime < DateTime.Now)
            {
                MessageBox.Show("Time of Availability value has expired", "CatSniper", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            Results.Document.Blocks.Clear();

            var authorization = Authorization.Text;
            var minecraftName = MinecraftName.Text.Trim();

            cancellationToken = new CancellationTokenSource();
            Task.Run(() => { ChangeName(authorization, minecraftName, dateTime, cancellationToken.Token); }, cancellationToken.Token);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            scheduler.Interrupt("myJob", cancellationToken.Token);
            cancellationToken.Cancel();
            ChangeState(true);
            Dispatcher.Invoke(new Action(() =>
            {
                AddText("Cancelled");
            }));
        }

        private async void ChangeName(string authorization, string minecraftName, DateTime expiryTime, CancellationToken token)
        {
            ChangeState(false);

            Dispatcher.Invoke(new Action(() =>
            {
                AddText("Session start");
                AddText("Minecraft name: " + minecraftName);
                AddText("Time of Availability: " + expiryTime.ToString("G"));
                AddText("Checking name status...");
            }));

            bool available = true;

            try
            {
                available = await CheckNameStatus(minecraftName, authorization, token);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(new Action(() =>
                {
                    AddText("Error: " +ex.Message);
                }));
                ChangeState(true);
                return;
            }

            if (available)
            {
                job.JobDataMap.Clear();
                job.JobDataMap.Add("authorization", authorization);
                job.JobDataMap.Add("minecraftName", minecraftName);

                ISimpleTrigger trigger = (ISimpleTrigger)TriggerBuilder.Create()
                     .WithIdentity("trigger1")
                     .StartAt(expiryTime) // some Date 
                     .ForJob("myJob") // identify job with name, group strings
                     .Build();

                await scheduler.Clear();
                await scheduler
                    .ScheduleJob(job, trigger, token);
            }
            
            //Thread.Sleep(5000);

        }

        private async Task<bool> CheckNameStatus(string minecraftName, string authorization, CancellationToken token)
        {
            HttpClient client = new HttpClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", authorization);

            var url = $"https://api.minecraftservices.com/minecraft/profile/name/{minecraftName}/available";
            MinecraftNameStatus? response = await client.GetFromJsonAsync<MinecraftNameStatus>(url,token);

            if (response == null)
            {
                Dispatcher.Invoke(new Action(() =>
                {
                    AddText("Could not get Minecraft name status");
                }));
                return false;
            }
            else
            {
                if (response.Status == "DUPLICATE")
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        AddText($"Minecraft Name {minecraftName} is currently not available");
                    }));

                    return true;
                }
                else if (response.Status == "AVAILABLE")
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        AddText($"Minecraft Name {minecraftName} is currently available!");
                    }));

                    return false;
                }
                else
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        AddText($"Minecraft Name {minecraftName} is unknown: ${response.Status}");
                    }));

                    return false;
                }
            }
        }

        public new void AddText(string text)
        {
            Results.AppendText("\r" + DateTime.Now.ToString("HH:mm:ss.fff")+" " + text );
        }

        public void ChangeState(bool mode)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                Authorization.IsEnabled = mode;
                MinecraftName.IsEnabled = mode;
                ExpiryDate.IsEnabled = mode;
                Request.IsEnabled = mode;

                Cancel.Visibility = mode?Visibility.Collapsed:Visibility.Visible;
            }));
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo() { FileName = e.Uri.AbsoluteUri, UseShellExecute= true });
            e.Handled = true;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StdSchedulerFactory factory = new StdSchedulerFactory();

            // get a scheduler
            scheduler = await factory.GetScheduler();
            await scheduler.Start();

            // define the job and tie it to our HelloJob class
            job = JobBuilder.Create<HelloJob>()
                .WithIdentity("myJob")
                //.UsingJobData("authorization", authorization)
                //.UsingJobData("minecraftName", minecraftName)
                .Build();

        }
    }
}
