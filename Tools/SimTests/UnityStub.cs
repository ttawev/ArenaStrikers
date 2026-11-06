// Заглушки атрибутов Unity, чтобы Tuning.cs собирался без Unity.
using System;
namespace UnityEngine
{
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class HideInInspector : Attribute { }
    public class SerializeField : Attribute { }
    public class SpaceAttribute : Attribute { public SpaceAttribute() { } public SpaceAttribute(float h) { } }
}
