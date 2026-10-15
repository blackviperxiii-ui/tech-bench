// [SelfTestCase] attribute + discovery runner (rearch stage 4).
// SelfTest.Main becomes a thin runner; feature files register cases with the attribute.
using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Marks a parameterless static void method on <see cref="SelfTest"/> as a self-test case.
/// The runner prints <see cref="Title"/> and invokes methods in <see cref="Order"/> then title order.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
sealed class SelfTestCaseAttribute : Attribute
{
    public readonly string Title;
    public int Order;

    public SelfTestCaseAttribute(string title)
    {
        Title = title == null ? "" : title;
    }
}

static partial class SelfTest
{
    /// <summary>Discover and run every [SelfTestCase] on this partial type.</summary>
    static void RunAttributedCases()
    {
        MethodInfo[] methods = typeof(SelfTest).GetMethods(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var cases = new List<CaseEntry>();
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo m = methods[i];
            object[] attrs = m.GetCustomAttributes(typeof(SelfTestCaseAttribute), false);
            if (attrs == null || attrs.Length == 0) continue;
            var attr = (SelfTestCaseAttribute)attrs[0];
            ParameterInfo[] ps = m.GetParameters();
            if (ps != null && ps.Length > 0)
            {
                Check("SelfTestCase must be parameterless (" + m.Name + ")", false, ps.Length + " args");
                continue;
            }
            if (m.ReturnType != typeof(void))
            {
                Check("SelfTestCase must return void (" + m.Name + ")", false, m.ReturnType.Name);
                continue;
            }
            cases.Add(new CaseEntry { Method = m, Attr = attr });
        }
        cases.Sort(CompareCases);
        // Exact count: a missing [SelfTestCase] must fail the run, not hide under a loose floor.
        // Bump ExpectedCaseCount when adding a case; never lower it to silence a drop.
        const int ExpectedCaseCount = 46;
        Check("SelfTestCase runner found cases",
            cases.Count == ExpectedCaseCount,
            "count=" + cases.Count + " expected=" + ExpectedCaseCount);

        for (int i = 0; i < cases.Count; i++)
        {
            CaseEntry c = cases[i];
            if (i > 0) Console.WriteLine();
            Console.WriteLine("== " + c.Attr.Title + " ==");
            try
            {
                c.Method.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Check("case threw (" + c.Attr.Title + ")", false, inner.GetType().Name + ": " + inner.Message);
            }
        }
    }

    static int CompareCases(CaseEntry a, CaseEntry b)
    {
        int o = a.Attr.Order.CompareTo(b.Attr.Order);
        if (o != 0) return o;
        return string.Compare(a.Attr.Title, b.Attr.Title, StringComparison.Ordinal);
    }

    sealed class CaseEntry
    {
        public MethodInfo Method;
        public SelfTestCaseAttribute Attr;
    }
}
