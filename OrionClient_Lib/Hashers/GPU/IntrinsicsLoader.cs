using ILGPU;
using ILGPU.Backends.OpenCL;
using ILGPU.Backends.PTX;
using ILGPU.IR;
using ILGPU.IR.Intrinsics;
using System.Reflection;

namespace OrionClientLib.Hashers.GPU
{
    internal class IntrinsicsLoader
    {
        public static void Load(Type t, Context context)
        {
            var methods = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute(typeof(IntrinsicMethodAttribute)) is IntrinsicMethodAttribute att)
                {
                    context.GetIntrinsicManager().RegisterMethod(method, new PTXIntrinsic(t, att.GenerateMethod, IntrinsicImplementationMode.GenerateCode));

                    if (!String.IsNullOrEmpty(att.OpenCLMethod))
                    {
                        context.GetIntrinsicManager().RegisterMethod(method, new CLIntrinsic(t, att.OpenCLMethod, att.IsOpenCLRedirect ? IntrinsicImplementationMode.Redirect : IntrinsicImplementationMode.GenerateCode));
                    }
                }
            }
        }

        public static void Load<T>(Context context)
        {
            Load(typeof(T), context);
        }
    }
}
