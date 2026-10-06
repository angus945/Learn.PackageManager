# module.verification.system-fact

`module.verification.system-fact` 定義跨模組共用的「系統事實（System Fact）」契約。

System Fact 表示：

> 系統中已經發生的事實。

例如：

```text
DamageApplied
QuestCompleted
SaveLoaded
CommandCompleted
TickCompleted
```

本 Module 定義唯一的 factual language，以及初始化期建構、執行期不可變的觀察路由。

## Public API

```csharp
namespace Module.Verification.SystemFact
{
    public interface ISystemFact
    {
    }

    public interface IDomainFact : ISystemFact
    {
    }

    public interface IApplicationFact : ISystemFact
    {
    }

    public interface IExecutionFact : ISystemFact
    {
    }
}
```

### `IDomainFact`

描述 Domain 中已經發生的事情。

```text
DamageApplied
CharacterKilled
QuestCompleted
```

### `IApplicationFact`

描述 Application Use Case 或協調流程產生的事情。

```text
SaveImported
DefinitionReloaded
GameSessionStarted
```

### `IExecutionFact`

描述 Runtime 或執行機制發生的事情。

```text
CommandStarted
CommandCompleted
CommandFailed
TickStarted
TickCompleted
```

## Fact 的擁有權

具體 Fact 應由真正擁有該語意的 Module 定義。

```text
module.combat
└── DamageApplied : IDomainFact

module.quest
└── QuestCompleted : IDomainFact

module.fixed-tick
└── TickCompleted : IExecutionFact
```

`module.verification.system-fact` 不集中保存所有 Fact。

## 設計規則

Fact 應描述「已經發生」的事情：

```text
DamageApplied     ✓
QuestCompleted    ✓

ApplyDamage       ✗
CompleteQuest     ✗
```

Fact 建立後應保持不可變。

Fact 本身不包含 hub observation metadata，例如：

```text
Tick
TraceId
CorrelationId
```

這些應由外部 Observability 系統附加。

`FactObservationContext` 的 `Sequence` 是單一 hub 接受每次合法 Publish 的 global monotonic observation order，`Timestamp` 是該次接受時間。它們不是 tick、domain version、跨程序排序或 persistent id。

## 不負責

本 Module 不提供：

```text
Event Bus
Logging
Metrics
Tracing
Replay
Serialization
Unity Integration
```

這些能力應由其他 Module 或 Framework 提供。

## 核心原則

`module.verification.system-fact` 只回答：

> **這是一個什麼類型的、已經發生的系統事實？**

它不處理：

> 如何保存 trace、控制 runtime、判斷 expectation 或持久化？

`SystemFactHubBuilder` 只在 Build 前註冊 assignable routes。Build 後 topology immutable；observer 失敗由 `IObservationFailureSink` 回報且不破壞 producer flow。此 public contract 已 freeze。
