using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClaudeCodeForVS.Converters
{
    public class RoleToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                if (role == "User")
                {
                    return new SolidColorBrush(Color.FromRgb(0, 122, 204)); // VS Blue / VS 蓝
                }
                else if (role == "Assistant")
                {
                    return new SolidColorBrush(Color.FromRgb(240, 240, 240)); // Light Gray / 浅灰
                }
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
