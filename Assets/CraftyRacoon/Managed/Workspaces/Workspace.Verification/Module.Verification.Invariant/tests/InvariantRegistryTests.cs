using System;
using Xunit;

namespace Module.Verification.Invariant.Tests
{
    public sealed class InvariantRegistryTests
    {
        [Fact]
        public void RegistrationSealsAndChecksUseOrdinalOrder()
        {
            InvariantRegistry<int> registry = new InvariantRegistry<int>();
            registry.Register(new Check("z")); registry.Register(new Check("a"));
            Assert.Throws<InvalidOperationException>(() => registry.Register(new Check("z")));
            Assert.Throws<InvalidOperationException>(() => registry.Evaluate(-1));
            registry.Seal();
            Assert.Throws<InvalidOperationException>(() => registry.Register(new Check("b")));
            Assert.Equal("a", registry.Evaluate(-1)[0].Code);
            Assert.Equal(2, registry.Count);
        }

        [Fact]
        public void PassingChecksReturnEmptyAndFailuresAreOwned()
        {
            InvariantRegistry<int> registry = new InvariantRegistry<int>();
            registry.Register(new Check("nonnegative")); registry.Seal();
            System.Collections.Generic.IReadOnlyList<InvariantViolation> first = registry.Evaluate(-1);
            Assert.Empty(registry.Evaluate(0));
            Assert.Single(first);
        }

        [Fact]
        public void RuleExceptionPropagatesForCallerPolicy()
        {
            InvariantRegistry<int> registry = new InvariantRegistry<int>();
            registry.Register(new Check("throw")); registry.Seal();
            Assert.Throws<NotSupportedException>(() => registry.Evaluate(int.MinValue));
        }
        private sealed class Check : IInvariant<int>
        {
            public Check(string code) { Code = code; }
            public string Code { get; }
            public InvariantResult Evaluate(int value)
            {
                if (value == int.MinValue) throw new NotSupportedException("test rule failure");
                if (value < 0) return InvariantResult.Violated(new InvariantViolation(Code, "negative"));
                return InvariantResult.Satisfied();
            }
        }
    }
}
