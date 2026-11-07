using System.Reflection;
using System.Runtime.CompilerServices;
using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Burndown.Overview;
using NetArchTest.Rules;
using Shouldly;
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace Burndown.ArchitectureTests;

public class LayerTests
{
    private static readonly Assembly Common = typeof(AppClock).Assembly;
    private static readonly Assembly Firefly = typeof(FireflyModule).Assembly;
    private static readonly Assembly Overview = typeof(OverviewModule).Assembly;
    private static readonly Assembly Entry = typeof(EntryModule).Assembly;
    private static readonly Assembly Web = typeof(Program).Assembly;

    private static readonly string[] AllNamespaces =
    [
        "Burndown.Common",
        "Burndown.Firefly",
        "Burndown.Overview",
        "Burndown.Entry",
        "Burndown.Web",
    ];

    private static readonly Assembly[] ProductionAssemblies = [Common, Firefly, Overview, Entry, Web];

    private static string[] Forbidden(params string[] allowed) =>
        AllNamespaces.Where(ns => !allowed.Contains(ns)).ToArray();

    private static void ShouldNotDependOn(Assembly assembly, string own, params string[] allowed)
    {
        var result = Types
            .InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(Forbidden([own, .. allowed]))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(ArchTestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);

    [Fact]
    public void Common_depends_on_no_other_project() => ShouldNotDependOn(Common, "Burndown.Common");

    [Fact]
    public void Firefly_depends_only_on_Common() => ShouldNotDependOn(Firefly, "Burndown.Firefly", "Burndown.Common");

    [Fact]
    public void Overview_depends_only_on_Common_and_Firefly() =>
        ShouldNotDependOn(Overview, "Burndown.Overview", "Burndown.Common", "Burndown.Firefly");

    [Fact]
    public void Entry_depends_only_on_Common_and_Firefly() =>
        ShouldNotDependOn(Entry, "Burndown.Entry", "Burndown.Common", "Burndown.Firefly");

    [Fact]
    public void Public_types_are_limited_to_what_other_assemblies_need()
    {
        var allowedPublic = new HashSet<string>
        {
            "Burndown.Common.AppClock",
            "Burndown.Common.Error",
            "Burndown.Common.Result",
            "Burndown.Common.Result`1",
            "Burndown.Firefly.FireflyModule",
            "Burndown.Overview.OverviewModule",
            "Burndown.Entry.EntryModule",
            "Program",
        };

        var unexpected = ProductionAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsVisible && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Select(t => t.FullName!)
            .Where(name => !allowedPublic.Contains(name))
            .ToList();

        unexpected.ShouldBeEmpty("Make these types internal, or add them to the allow-list with a reason.");
    }

    [Fact]
    public void Production_code_does_not_use_Newtonsoft()
    {
        var result = Types
            .InAssemblies(ProductionAssemblies)
            .Should()
            .NotHaveDependencyOn("Newtonsoft.Json")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Production_code_does_not_read_the_system_clock_directly()
    {
        var offenders = new List<string>();
        foreach (var type in ProductionAssemblies.SelectMany(a => a.GetTypes()))
        {
            const BindingFlags flags =
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                var body = method.GetMethodBody();
                if (body?.GetILAsByteArray() is not { } il)
                {
                    continue;
                }

                if (CallsForbiddenClock(method, il))
                {
                    offenders.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Use AppClock/TimeProvider instead of DateTime.Now/UtcNow/Today or DateTimeOffset.Now/UtcNow."
        );
    }

    [Fact]
    public void Classes_do_not_use_primary_constructors()
    {
        // A primary constructor on a class makes the compiler emit parameter-backed fields with the parameter's
        // exact name (no leading underscore) that are never declared by hand: detect them by the missing convention.
        var offenders = ProductionAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null
                && !t.FullName!.Contains('<')
                && t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Any(f => f.GetCustomAttribute<CompilerGeneratedAttribute>() is not null && !f.Name.StartsWith('<'))
            )
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty("Declare private readonly fields and an explicit constructor instead.");
    }

    private static bool CallsForbiddenClock(MethodBase method, byte[] il)
    {
        var module = method.Module;
        for (var i = 0; i < il.Length - 4; i++)
        {
            // call (0x28) / callvirt (0x6F) followed by a 4-byte metadata token.
            if (il[i] is not (0x28 or 0x6F))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            MethodBase? target;
            try
            {
                target = module.ResolveMethod(
                    token,
                    method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null
                );
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
            {
                continue;
            }

            if (
                target is { Name: "get_Now" or "get_UtcNow" or "get_Today" }
                && target.DeclaringType == typeof(DateTime)
            )
            {
                return true;
            }

            if (target is { Name: "get_Now" or "get_UtcNow" } && target.DeclaringType == typeof(DateTimeOffset))
            {
                return true;
            }
        }

        return false;
    }
}
