using System;
using System.Linq;
using Khylo.Data.Enums;
using Xunit;

namespace Khylo.Server.Implementations.Tests.TypedBaseItem
{
    public class BaseItemKindTests
    {
        public static TheoryData<Type> BaseItemKind_TestData()
        {
            var data = new TheoryData<Type>();

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in loadedAssemblies)
            {
                if (IsProjectAssemblyName(assembly.FullName))
                {
                    var baseItemTypes = assembly.GetTypes()
                        .Where(targetType => targetType.IsClass
                                             && !targetType.IsAbstract
                                             && targetType.IsSubclassOf(typeof(Khylo.Controller.Entities.BaseItem)));
                    foreach (var baseItemType in baseItemTypes)
                    {
                        data.Add(baseItemType);
                    }
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(BaseItemKind_TestData))]
        public void EnumParse_GivenValidBaseItemType_ReturnsEnumValue(Type baseItemDescendantType)
        {
            var enumValue = Enum.Parse<BaseItemKind>(baseItemDescendantType.Name);
            Assert.True(Enum.IsDefined(enumValue));
        }

        [Theory]
        [MemberData(nameof(BaseItemKind_TestData))]
        public void GetBaseItemKind_WhenCalledAfterDefaultCtor_DoesNotThrow(Type baseItemDescendantType)
        {
            var defaultConstructor = baseItemDescendantType.GetConstructor(Type.EmptyTypes);
            var instance = (Khylo.Controller.Entities.BaseItem)defaultConstructor!.Invoke(null);
            var exception = Record.Exception(() => instance.GetBaseItemKind());
            Assert.Null(exception);
        }

        private static bool IsProjectAssemblyName(string? name)
        {
            if (name is null)
            {
                return false;
            }

            return name.StartsWith("Khylo", StringComparison.OrdinalIgnoreCase);
        }
    }
}
