using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.UnsafeKit
{
    // Ref fields require a .NET 7+ runtime. This unused helper is not available
    // on Unity's .NET Standard runtime; the normal disposal helpers stay intact.
#if NET7_0_OR_GREATER
    public readonly ref struct RefDisposable
    {
        public readonly ref struct Disposable<T> where T : IDisposable
        {
            public readonly ref T Instance;
            public readonly bool SetNullWhenDispose;

            internal Disposable(ref T instance, bool setNullWhenDispose)
            {
                Instance = ref instance;
                SetNullWhenDispose = setNullWhenDispose;
            }

            public readonly void Dispose()
            {
                Instance?.Dispose();
                Instance = default;
            }
        }
        public static Disposable<T> From<T>(ref T instance, bool setNullWhenDispose = false) where T : IDisposable
        {
            return new(ref instance, setNullWhenDispose);
        }
    }
#endif
}
