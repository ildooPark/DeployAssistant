using DeployAssistant.DataComponent;
using System;
using System.Globalization;
using System.Windows.Data;

namespace DeployAssistant.View
{
    /// <summary>
    /// Strips lifecycle bits from a <see cref="DataState"/> value and returns only
    /// the change-kind bits (<c>Added</c>, <c>Deleted</c>, <c>Modified</c>,
    /// <c>Restored</c>), so that XAML <c>DataTrigger</c> comparisons work correctly
    /// even when lifecycle flags (e.g. <c>IntegrityChecked</c>, <c>PreStaged</c>)
    /// are also set.
    ///
    /// <para>
    /// <b>Problem without this converter:</b> <c>DataState</c> is a <c>[Flags]</c>
    /// enum, so a row whose file has
    /// <c>DataState = Added | IntegrityChecked</c> would <em>not</em> match a
    /// <c>DataTrigger</c> whose <c>Value="Added"</c> because the comparison is
    /// an exact equality check.
    /// </para>
    ///
    /// <para>
    /// <b>Usage in XAML:</b>
    /// <code>
    /// &lt;DataTrigger Binding="{Binding DataState, Converter={StaticResource DataStateChangeKindConverter}}"
    ///              Value="Added"&gt;
    ///   ...
    /// &lt;/DataTrigger&gt;
    /// </code>
    /// </para>
    /// </summary>
    [System.Windows.Data.ValueConversion(typeof(DataState), typeof(DataState))]
    public sealed class DataStateChangeKindConverter : IValueConverter
    {
        /// <summary>
        /// Mask covering only the four change-kind bits; lifecycle bits are excluded.
        /// </summary>
        private const DataState ChangeKindMask =
            DataState.Added | DataState.Deleted | DataState.Modified | DataState.Restored;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataState state)
                return state & ChangeKindMask;

            return System.Windows.DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException(
                $"{nameof(DataStateChangeKindConverter)} is a one-way converter.");
    }
}
