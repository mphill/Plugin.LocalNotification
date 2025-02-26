﻿using System;
using Plugin.LocalNotification;
using Xamarin.Forms;

namespace LocalNotification.Sample
{
    public partial class NotificationPage : ContentPage
    {
        public NotificationPage(int id, string message, int tabCount)
        {
            InitializeComponent();

            IdLabel.Text = $"Id {id}";
            MessageLabel.Text = $"Message {message}";
            TapCountLabel.Text = $"Tap Count {tabCount}";
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            var deliveredNotificationList = await LocalNotificationCenter.Current.GetDeliveredNotificationList();

            if (deliveredNotificationList != null)
            {
                await DisplayAlert("Delivered Notification Count", deliveredNotificationList.Count.ToString(), "OK");
            }

            await Navigation.PopModalAsync();
        }
    }
}