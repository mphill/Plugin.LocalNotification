﻿using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Work;
using Java.Util.Concurrent;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Plugin.LocalNotification.Platform.Droid
{
    /// <inheritdoc />
    public class NotificationServiceImpl : INotificationService
    {
        /// <summary>
        ///
        /// </summary>
        protected readonly NotificationManager NotificationManager;

        /// <summary>
        ///
        /// </summary>
        protected readonly WorkManager WorkManager;

        public Dictionary<string, NotificationAction> NotificationActions { get; } = new Dictionary<string, NotificationAction>();

        /// <inheritdoc />
        public event NotificationTappedEventHandler NotificationTapped;

        /// <inheritdoc />
        public event NotificationReceivedEventHandler NotificationReceived;

        /// <inheritdoc />
        public void OnNotificationTapped(NotificationTappedEventArgs e)
        {
            NotificationTapped?.Invoke(e);
        }

        /// <inheritdoc />
        public void OnNotificationReceived(NotificationReceivedEventArgs e)
        {
            NotificationReceived?.Invoke(e);
        }

        /// <summary>
        ///
        /// </summary>
        public NotificationServiceImpl()
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.IceCreamSandwich)
                {
                    return;
                }

                NotificationManager =
                    Application.Context.GetSystemService(Context.NotificationService) as NotificationManager ??
                    throw new ApplicationException(Properties.Resources.AndroidNotificationServiceNotFound);
                WorkManager = WorkManager.GetInstance(Application.Context);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <inheritdoc />
        public bool Cancel(int notificationId)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.IceCreamSandwich)
                {
                    return false;
                }

                WorkManager?.CancelAllWorkByTag(notificationId.ToString(CultureInfo.CurrentCulture));
                NotificationManager?.Cancel(notificationId);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
        }

        /// <inheritdoc />
        public bool CancelAll()
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.IceCreamSandwich)
                {
                    return false;
                }

                WorkManager?.CancelAllWork();
                NotificationManager?.CancelAll();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
        }

        /// <inheritdoc />
        public Task<bool> Show(Func<NotificationRequestBuilder, NotificationRequest> builder) => Show(builder.Invoke(new NotificationRequestBuilder()));

        /// <inheritdoc />
        public Task<bool> Show(NotificationRequest notificationRequest)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.IceCreamSandwich)
                {
                    return Task.FromResult(false);
                }

                if (notificationRequest is null)
                {
                    return Task.FromResult(false);
                }

                var result = notificationRequest.Schedule.NotifyTime.HasValue ? ShowLater(notificationRequest) : ShowNow(notificationRequest);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="notificationRequest"></param>
        protected virtual bool ShowLater(NotificationRequest notificationRequest)
        {
            if (notificationRequest.Schedule.NotifyTime is null ||
                notificationRequest.Schedule.NotifyTime.Value <= DateTime.Now) // To be consistent with iOS, Do not Schedule notification if NotifyTime is earlier than DateTime.Now
            {
                return false;
            }

            Cancel(notificationRequest.NotificationId);

            return EnqueueWorker(notificationRequest);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="request"></param>
        protected internal virtual bool ShowNow(NotificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Android.ChannelId))
            {
                request.Android.ChannelId = AndroidOptions.DefaultChannelId;
            }

            using var builder = new NotificationCompat.Builder(Application.Context, request.Android.ChannelId);
            builder.SetContentTitle(request.Title);

            builder.SetSubText(request.Subtitle);


            builder.SetContentText(request.Description);
            using (var bigTextStyle = new NotificationCompat.BigTextStyle())
            {
                var bigText = bigTextStyle.BigText(request.Description);
                builder.SetStyle(bigText);
            }
            builder.SetNumber(request.BadgeNumber);
            builder.SetAutoCancel(request.Android.AutoCancel);
            builder.SetOngoing(request.Android.Ongoing);

            if (string.IsNullOrWhiteSpace(request.Android.Group) == false)
            {
                builder.SetGroup(request.Android.Group);
                if (request.Android.IsGroupSummary)
                {
                    builder.SetGroupSummary(true);
                }
            }

            if (request.Category != NotificationCategoryTypes.None && Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                builder.SetCategory(ToNativeCategory(request.Category));
            }

            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            {
                builder.SetPriority((int)request.Android.Priority);

                var soundUri = NotificationCenter.GetSoundUri(request.Sound);
                if (soundUri != null)
                {
                    builder.SetSound(soundUri);
                }
            }

            if (request.Android.VibrationPattern != null)
            {
                builder.SetVibrate(request.Android.VibrationPattern);
            }

            if (request.Android.ProgressBarMax.HasValue &&
                request.Android.ProgressBarProgress.HasValue &&
                request.Android.ProgressBarIndeterminate.HasValue)
            {
                builder.SetProgress(request.Android.ProgressBarMax.Value,
                    request.Android.ProgressBarProgress.Value,
                    request.Android.ProgressBarIndeterminate.Value);
            }

            if (request.Android.Color.HasValue)
            {
                builder.SetColor(request.Android.Color.Value);
            }

            builder.SetSmallIcon(GetIcon(request.Android.IconSmallName));
            if (string.IsNullOrWhiteSpace(request.Android.IconLargeName) == false)
            {
                builder.SetLargeIcon(BitmapFactory.DecodeResource(Application.Context.Resources, GetIcon(request.Android.IconLargeName)));
            }

            if (request.Android.TimeoutAfter.HasValue)
            {
                builder.SetTimeoutAfter((long)request.Android.TimeoutAfter.Value.TotalMilliseconds);
            }

            var notificationIntent = Application.Context.PackageManager?.GetLaunchIntentForPackage(Application.Context.PackageName ?? string.Empty);
            if (notificationIntent is null)
            {
                Log($"NotificationServiceImpl.ShowNow: notificationIntent is null");
                return false;
            }

            var serializedNotification = ObjectSerializer.SerializeObject(request);
            notificationIntent.SetFlags(ActivityFlags.SingleTop);
            notificationIntent.PutExtra(NotificationCenter.ReturnRequest, serializedNotification);

            var pendingIntent = PendingIntent.GetActivity(Application.Context, request.NotificationId, notificationIntent,
                PendingIntentFlags.CancelCurrent);
            builder.SetContentIntent(pendingIntent);


            if (NotificationActions.Count > 0)
            {
                foreach(var notificationAction in NotificationActions)
                {
                    var action = new NotificationCompat.Action(GetIcon(request.Android.IconSmallName), new Java.Lang.String(notificationAction.Value.Title), pendingIntent);

                    builder.AddAction(action);
                }
            }

            var notification = builder.Build();
            if (Build.VERSION.SdkInt < BuildVersionCodes.O &&
                request.Android.LedColor.HasValue)
            {
                notification.LedARGB = request.Android.LedColor.Value;
            }

            if (Build.VERSION.SdkInt < BuildVersionCodes.O &&
                string.IsNullOrWhiteSpace(request.Sound))
            {
                notification.Defaults = NotificationDefaults.All;
            }
            NotificationManager?.Notify(request.NotificationId, notification);

            var args = new NotificationReceivedEventArgs
            {
                Request = request
            };
            NotificationCenter.Current.OnNotificationReceived(args);

            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="request"></param>
        protected internal virtual bool EnqueueWorker(NotificationRequest request)
        {
            if (!request.Schedule.NotifyTime.HasValue)
            {
                Log($"{nameof(request.Schedule.NotifyTime)} value doesn't set!");
                return false;
            }

            var notifyTime = request.Schedule.NotifyTime.Value;

            using var dataBuilder = new Data.Builder();
            var dictionary = NotificationCenter.GetRequestSerialize(request);
            foreach (var item in dictionary)
            {
                dataBuilder.PutString(item.Key, item.Value);
            }
            var data = dataBuilder.Build();
            var tag = request.NotificationId.ToString(CultureInfo.CurrentCulture);
            var diff = (long)(notifyTime - DateTime.Now).TotalMilliseconds;

            var workRequest = OneTimeWorkRequest.Builder.From<ScheduledNotificationWorker>()
                .AddTag(tag)
                .SetInputData(data)
                .SetInitialDelay(diff, TimeUnit.Milliseconds)
                .Build();

            WorkManager?.Enqueue(workRequest);
            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="iconName"></param>
        /// <returns></returns>
        protected static int GetIcon(string iconName)
        {
            var iconId = 0;
            if (string.IsNullOrWhiteSpace(iconName) == false)
            {
                iconId = Application.Context.Resources?.GetIdentifier(iconName, "drawable", Application.Context.PackageName) ?? 0;
            }

            if (iconId != 0)
            {
                return iconId;
            }

            iconId = Application.Context.ApplicationInfo?.Icon ?? 0;
            if (iconId == 0)
            {
                iconId = Application.Context.Resources?.GetIdentifier("icon", "drawable",
                    Application.Context.PackageName) ?? 0;
            }

            return iconId;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="message"></param>
        protected static void Log(string message)
        {
            Android.Util.Log.Info(Application.Context.PackageName, message);
        }

        public void RegisterCategories(NotificationCategory[] notificationCategories)
        {
            foreach(var category in notificationCategories)
            {
                RegisterActions(category.NotificationActions);
            }
        }
        private void RegisterActions(NotificationAction[] notificationActions)
        {
            foreach(var action in notificationActions)
            {
                NotificationActions.Add(action.Identifier, action);
            }
        }

        private string ToNativeCategory(NotificationCategoryTypes type)
        {
            switch (type)
            {
                case NotificationCategoryTypes.None:
                    return NotificationCompat.CategoryStatus;

                case NotificationCategoryTypes.Alarm:
                    return NotificationCompat.CategoryAlarm;

                case NotificationCategoryTypes.Reminder:
                    return NotificationCompat.CategoryReminder;

                case NotificationCategoryTypes.Event:
                    return NotificationCompat.CategoryEvent;

                case NotificationCategoryTypes.System:
                    return NotificationCompat.CategorySystem;

                default:
                    return NotificationCompat.CategoryStatus;
            }
        }
    }
}