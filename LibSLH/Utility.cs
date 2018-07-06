using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace LibSLH
{
    public static class Utility
    {
        public static string GetPassword()
        {
            var password = "";
            while (true)
            {
                var i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (i.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password.Remove(password.Length - 1);
                    }
                }
                else
                {
                    password += i.KeyChar;
                }
            }
            return password;
        }

        public static object Convert(Type type, object @object)
        {
            if (type.IsEnum)
            {
                if (@object is string)
                    return Enum.Parse(type, (string)@object);
                return Enum.ToObject(type, @object);
            }

            if (@object is int)
                return (Int32)@object;

            if (@object is long)
                return (Int64)@object;

            var converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertFrom(@object.GetType()))
                return TypeDescriptor.GetConverter(type).ConvertFrom(@object);

            return @object; // Good luck...
        }

        public class BoundMemberInfo
        {
            public readonly object Target;
            public readonly MemberInfo MemberInfo;

            public BoundMemberInfo(object target, MemberInfo member_info)
            {
                Target = target;
                MemberInfo = member_info;
            }
        }

        public static async Task<object> EvalMemberPath(
            object target,
            string member_path,
            object[] args,
            bool set_value)
        {
            var method_path_elements = member_path.Split('.');
            return await EvalMemberPath(
                target,
                method_path_elements.ToList(),
                method_path_elements.FirstOrDefault(),
                args,
                set_value,
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.GetField |
                BindingFlags.SetField |
                BindingFlags.GetProperty |
                BindingFlags.SetProperty |
                BindingFlags.InvokeMethod);
        }

        public static async Task<BoundMemberInfo> EvalMemberInfoPath(
            object target,
            string member_path,
            object[] args,
            bool set_value)
        {
            var method_path_elements = member_path.Split('.');
            return await EvalMemberInfoPath(
                target,
                method_path_elements.ToList(),
                method_path_elements.FirstOrDefault(),
                args,
                set_value,
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.GetField |
                BindingFlags.SetField |
                BindingFlags.GetProperty |
                BindingFlags.SetProperty |
                BindingFlags.InvokeMethod);
        }

        public static async Task<BoundMemberInfo> EvalMemberInfoPath(
            object target,
            List<String> member_path_elements,
            string current_member_path,
            object[] args,
            bool set_value,
            BindingFlags flags)
        {
            if (member_path_elements.Count == 0)
                return new BoundMemberInfo(target, null);
            var target_type = target.GetType();
            var member_name = member_path_elements.First();
            var member_infos = target_type.GetMember(member_name, flags);
            if (!member_infos.Any())
                throw new Exception("Member not found");
            var member_info = member_infos.FirstOrDefault();

            if (member_path_elements.Count == 1)
            {
                return new BoundMemberInfo(target, member_info);
            }
            else
            {
                object member_target;
                switch (member_info.MemberType)
                {
                    case MemberTypes.Field:
                        member_target = ((FieldInfo)member_info).GetValue(target);
                        break;

                    case MemberTypes.Property:
                        member_target = ((PropertyInfo)member_info).GetValue(target);
                        break;

                    default:
                    case MemberTypes.Method:
                        member_target = await Task.Run(() => ((MethodInfo)member_info).Invoke(target, null));
                        break;
                }

                var next_member_path_elements = member_path_elements.Skip(1).ToList();
                var next_member_path = current_member_path + "." + next_member_path_elements.First();
                return await EvalMemberInfoPath(member_target, next_member_path_elements, next_member_path, args, set_value, flags);
            }
        }

        public static async Task<object> EvalMemberPath(
            object target,
            List<String> member_path_elements,
            string current_member_path,
            object[] args,
            bool set_value,
            BindingFlags flags)
        {
            var bound_member_info = await EvalMemberInfoPath(target, member_path_elements, current_member_path, args, set_value, flags);
            var member_target = bound_member_info.Target;
            var member_info = bound_member_info.MemberInfo;
            switch (member_info.MemberType)
            {
                case MemberTypes.Field:
                    if (set_value)
                        ((FieldInfo)member_info).SetValue(member_target, args[0]);
                    else
                        return ((FieldInfo)member_info).GetValue(member_target);
                    break;

                case MemberTypes.Property:
                    if (set_value)
                        ((PropertyInfo)member_info).SetValue(member_target, args[0]);
                    else
                        return ((PropertyInfo)member_info).GetValue(member_target);
                    break;

                default:
                case MemberTypes.Method:
                    var method_info = ((MethodInfo)member_info);
                    var parameters = method_info.GetParameters();
                    var args_converted = parameters
                        .Zip(args, (p, a) => new { p, a })
                        .Select(e => Convert(e.p.ParameterType, e.a))
                        .ToArray();
                    var result = method_info.Invoke(member_target, args_converted);
                    if (result is Task)
                        return await (dynamic)result;
                    return result;
            }
            return args[0];
        }

        public static Delegate WrapDynamicDelegate(Type delegate_type, DynamicDelegate dynamic_delegate)
        {
            var delegate_parameter_types =
                delegate_type
                .GetMethod("Invoke")
                .GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();
            var event_handler_parameter_expressions =
                delegate_parameter_types
                .Zip(Enumerable.Range(0, delegate_parameter_types.Count()), (type, i) => new { type, name = $"_{i}" })
                .Select(e => Expression.Parameter(e.type, e.name))
                .ToArray();

            // parameter_array = new object[] { _1, _2, ... , _N };
            var parameter_array_expression = Expression.NewArrayInit(typeof(object), event_handler_parameter_expressions);
            // Some magical binding/glue right here
            Expression<DynamicDelegate> dynamic_expression = (objects) => dynamic_delegate(objects);
            // dynamic_delegate(parameter_array)
            var invoke = Expression.Invoke(dynamic_expression, parameter_array_expression);
            // Typed wrapper
            var wrapped = Expression.Lambda(delegate_type, invoke, event_handler_parameter_expressions);
            return wrapped.Compile();
        }

        public static Vector3d RegionHandleToGlobalCoordinates(ulong handle)
        {
            Utils.LongToUInts(handle, out uint x, out uint y);
            return new Vector3d(x, y, 0.0);
        }
    }
}