using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DDEN.Shared
{
    /// <summary>
    /// 反射工具：不硬引用 CasualtiesMP / QoL，装了就用，没装就降级。
    /// 编译期只依赖 Assembly-CSharp + BepInEx + CUCoreLib。
    /// </summary>
    public static class Reflect
    {
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static Assembly FindAssembly(string simpleName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>按 "Namespace.Type" 或 "Namespace.Outer+Nested" 找类型，找不到返回 null。</summary>
        public static Type Type(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            if (TypeCache.TryGetValue(fullName, out Type cached) && cached != null) return cached;

            Type t = Resolve(fullName);
            if (t != null) TypeCache[fullName] = t;
            return t;
        }

        private static Type Resolve(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static readonly BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static FieldInfo Field(Type t, string name, bool isStatic = true)
        {
            if (t == null) return null;
            return t.GetField(name, isStatic ? AllStatic : AllInstance);
        }

        public static PropertyInfo Prop(Type t, string name, bool isStatic = true)
        {
            if (t == null) return null;
            return t.GetProperty(name, isStatic ? AllStatic : AllInstance);
        }

        public static object GetStatic(string typeName, string member)
        {
            return GetStatic(Type(typeName), member);
        }

        public static ObjectMember GetMember(string typeName, string member)
        {
            Type t = Type(typeName);
            if (t == null) return null;
            FieldInfo f = Field(t, member);
            if (f != null) return new ObjectMember(t, f, null);
            PropertyInfo p = Prop(t, member);
            if (p != null) return new ObjectMember(t, null, p);
            return null;
        }

        public static object GetStatic(Type t, string member)
        {
            if (t == null) return null;
            try
            {
                FieldInfo f = Field(t, member);
                if (f != null) return f.GetValue(null);
                PropertyInfo p = Prop(t, member);
                if (p != null && p.CanRead) return p.GetValue(null, null);
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.GetStatic({t.Name}.{member}) failed: {ex.Message}");
            }
            return null;
        }

        public static bool TryGetStatic<T>(string typeName, string member, out T value)
        {
            value = default;
            object raw = GetStatic(typeName, member);
            if (raw is T typed)
            {
                value = typed;
                return true;
            }
            return false;
        }

        public static void SetStatic(Type t, string member, object value)
        {
            if (t == null) return;
            try
            {
                FieldInfo f = Field(t, member);
                if (f != null)
                {
                    f.SetValue(null, Coerce(value, f.FieldType));
                    return;
                }
                PropertyInfo p = Prop(t, member);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(null, Coerce(value, p.PropertyType), null);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.SetStatic({t.Name}.{member}) failed: {ex.Message}");
            }
        }

        public static void SetStatic(string typeName, string member, object value)
        {
            SetStatic(Type(typeName), member, value);
        }

        public static object GetInstance(object target, string member)
        {
            if (target == null) return null;
            try
            {
                Type t = target.GetType();
                FieldInfo f = Field(t, member, false);
                if (f != null) return f.GetValue(target);
                PropertyInfo p = Prop(t, member, false);
                if (p != null && p.CanRead) return p.GetValue(target, null);
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.GetInstance({member}) failed: {ex.Message}");
            }
            return null;
        }

        public static bool TrySetInstance(object target, string member, object value)
        {
            if (target == null) return false;
            try
            {
                Type t = target.GetType();
                FieldInfo f = Field(t, member, false);
                if (f != null)
                {
                    f.SetValue(target, Coerce(value, f.FieldType));
                    return true;
                }
                PropertyInfo p = Prop(t, member, false);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(target, Coerce(value, p.PropertyType), null);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.TrySetInstance({member}) failed: {ex.Message}");
            }
            return false;
        }

        public static MethodInfo Method(Type t, string name, int paramCount = -1)
        {
            if (t == null) return null;
            return t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == name && (paramCount < 0 || m.GetParameters().Length == paramCount));
        }

        public static object InvokeStatic(string typeName, string method, params object[] args)
        {
            return InvokeStatic(Type(typeName), method, args);
        }

        public static object InvokeStatic(Type t, string method, params object[] args)
        {
            if (t == null) return null;
            try
            {
                MethodInfo mi = Method(t, method, args?.Length ?? 0);
                return mi?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.InvokeStatic({t.Name}.{method}) failed: {ex.Message}");
                return null;
            }
        }

        public static object InvokeInstance(object target, string method, params object[] args)
        {
            if (target == null) return null;
            try
            {
                MethodInfo mi = Method(target.GetType(), method, args?.Length ?? 0);
                return mi?.Invoke(target, args);
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.InvokeInstance({method}) failed: {ex.Message}");
                return null;
            }
        }

        public static object Coerce(object value, Type target)
        {
            if (value == null || target == null) return value;
            if (target.IsInstanceOfType(value)) return value;
            try
            {
                if (target == typeof(bool)) return Convert.ToBoolean(value);
                if (target == typeof(int)) return Convert.ToInt32(value);
                if (target == typeof(float)) return Convert.ToSingle(value);
                if (target == typeof(double)) return Convert.ToDouble(value);
                if (target == typeof(string)) return Convert.ToString(value);
                if (target.IsEnum) return Enum.Parse(target, Convert.ToString(value), true);
            }
            catch
            {
                // 静默：值不合法就保持原样，交给上层判定
            }
            return value;
        }

        /// <summary>读结构体静态字段并写回（Together.Multiplayer.rules 是 struct）。</summary>
        public static bool MutateStructMember(string typeName, string structMember, Action<object> mutate)
        {
            Type t = Type(typeName);
            if (t == null) return false;
            try
            {
                FieldInfo f = Field(t, structMember);
                if (f == null) return false;
                object boxed = f.GetValue(null);
                if (boxed == null) return false;
                mutate(boxed);
                f.SetValue(null, boxed);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"Reflect.MutateStructMember({typeName}.{structMember}) failed: {ex.Message}");
                return false;
            }
        }
    }

    public sealed class ObjectMember
    {
        public readonly Type DeclaringType;
        private readonly FieldInfo _field;
        private readonly PropertyInfo _prop;

        internal ObjectMember(Type declaring, FieldInfo field, PropertyInfo prop)
        {
            DeclaringType = declaring;
            _field = field;
            _prop = prop;
        }

        public bool IsStatic
        {
            get
            {
                if (_field != null) return _field.IsStatic;
                if (_prop != null) return _prop.GetMethod?.IsStatic ?? false;
                return true;
            }
        }

        public Type ValueType => _field != null ? _field.FieldType : _prop?.PropertyType;
    }
}
