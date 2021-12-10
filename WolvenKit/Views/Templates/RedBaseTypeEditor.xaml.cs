using System;
using System.Windows;
using System.Windows.Controls;
using WolvenKit.RED4.Types;

namespace WolvenKit.Views.Editors
{
    /// <summary>
    /// Interaction logic for RedBaseTypeEditor.xaml
    /// </summary>
    public partial class RedBaseTypeEditor : UserControl
    {
        public RedBaseTypeEditor()
        {
            InitializeComponent();
        }

        public IRedType RedType
        {
            get => (IRedType)this.GetValue(RedTypeProperty);
            set => this.SetValue(RedTypeProperty, value);
        }
        public static readonly DependencyProperty RedTypeProperty = DependencyProperty.Register(
            nameof(RedType), typeof(IRedType), typeof(RedBaseTypeEditor), new PropertyMetadata(default(IRedType)));


        public string Text
        {
            get => GetValueFromRedValue();
            set => SetRedValue(value);
        }

        private void SetRedValue(string value)
        {
            if (RedType is CName)
            {
                SetCurrentValue(RedTypeProperty, (CName)value);
            }
            else if (RedType is CString)
            {
                SetCurrentValue(RedTypeProperty, (CString)value);
            }

        }
        public Type PropertyType => RedType?.GetType() ?? null;

        private string GetValueFromRedValue()
        {
            if (RedType == null)
            {
                return "null";
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedString)))
            {
                var value = (IRedString)RedType;
                if (value.GetValue() == "")
                {
                    return "null";
                }
                else
                {
                    return value.GetValue();
                }
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedArray)))
            {
                var value = (IRedArray)RedType;
                return $"Array: {value.InnerType.Name} [{value.Count}]";
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedBaseHandle)))
            {
                var value = (IRedBaseHandle)RedType;
                return $"Handle: {value.InnerType.Name}";
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedEnum)))
            {
                var value = (IRedEnum)RedType;
                return value.ToEnumString();
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedBitField)))
            {
                var value = (IRedBitField)RedType;
                return value.ToBitFieldString();
            }
            else if (PropertyType.IsAssignableTo(typeof(CBool)))
            {
                var value = (CBool)RedType;
                return value ? "True" : "False";
            }
            else if (PropertyType.IsAssignableTo(typeof(CRUID)))
            {
                var value = (CRUID)RedType;
                return ((ulong)value).ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(CUInt64)))
            {
                var value = (CUInt64)RedType;
                return ((ulong)value).ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedInteger)))
            {
                var value = (IRedInteger)RedType;
                return (value switch
                {
                    CUInt8 uint64 => (float)uint64,
                    CInt8 uint64 => (float)uint64,
                    CInt16 uint64 => (float)uint64,
                    CUInt16 uint64 => (float)uint64,
                    CInt32 uint64 => (float)uint64,
                    CUInt32 uint64 => (float)uint64,
                    CInt64 uint64 => (float)uint64,
                    _ => throw new ArgumentOutOfRangeException(nameof(value)),
                }).ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(FixedPoint)))
            {
                var value = (FixedPoint)RedType;
                return ((float)value).ToString("R");
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedPrimitive<float>)))
            {
                var value = (IRedPrimitive)RedType;
                return ((float)(CFloat)value).ToString("R");
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedRef)))
            {
                var value = (IRedRef)RedType;
                if (value.DepotPath != "")
                {
                    return value.DepotPath;
                }
                else
                {
                    return "null";
                }
            }
            else
            {
                return PropertyType?.Name ?? "null";
            }
        }


    }
}
