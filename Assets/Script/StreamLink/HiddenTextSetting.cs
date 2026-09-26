using System;

namespace YARG.Settings.Types
{
    /// <summary>
    /// A string setting that is saved to settings.json but has no menu visual.
    /// </summary>
    public class HiddenTextSetting : AbstractSetting<string>
    {
        public override string AddressableName => throw new NotSupportedException(
            "HiddenTextSetting has no visual and can't be added to a settings tab");

        public HiddenTextSetting(string value, Action<string> onChange = null) : base(onChange)
        {
            _value = value ?? "";
        }

        protected override void SetValue(string value)
        {
            _value = value ?? "";
        }

        public override bool ValueEquals(string value)
        {
            return value == Value;
        }
    }
}
