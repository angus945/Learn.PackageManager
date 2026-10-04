# Module.Verification.SystemFact.Observability

`Module.Verification.SystemFact.Observability` 提供 `ISystemFact` 的觀察與分派機制。

依賴：

```text
Module.Verification.SystemFact
```

## 責任

本 Module 負責：

- 接收 `ISystemFact`
- 建立 Observation Context
- 將 Fact 路由至對應 Observer
- 隔離 Observer 失敗，避免影響主流程

核心流程：

```text
ISystemFact
    ↓
ISystemFactSink
    ↓
SystemFactHub
    ↓
ISystemFactObserver<TFact>
```

## 主要 API

```text
ISystemFactSink
ISystemFactObserver<TFact>
ObservedFact
FactObservationContext
SystemFactHub
```

## 使用概念

```text
DamageApplied
    ↓
SystemFactHub
    ├── LogObserver
    ├── MetricsObserver
    └── DebugOverlayObserver
```

Producer 不需要知道有哪些 Observer。

Observer 的加入或移除也不應要求修改 Domain 或 Application 邏輯。

## Observation Context

Fact 本身只描述「發生了什麼」。

觀察相關資訊由本 Module 額外提供，例如：

```text
Publish Sequence
Timestamp
```

`FactObservationContext` 只保留已有穩定語意的欄位。每次合法 `Publish` 都配置 sequence，即使沒有 observer。`SystemFactHubBuilder` 在 composition 時註冊 assignable/interface routes，`Build` 後 topology immutable。

`Publish(null)` 固定拋出 `ArgumentNullException`。Observer failure 不影響主流程，並透過 `IObservationFailureSink` 回報。

Routing 使用 assignable semantics，因此 concrete、category interface 與 `ISystemFact` observers 會收到同一個 fact 與 sequence。此 public contract 已 freeze。

## 不負責

本 Module 不負責：

```text
具體 Domain Fact 定義
Domain Event Bus
Logging 實作
Metrics Backend
Tracing Backend
Replay
Persistence
Unity Integration
```

具體輸出應由外部 Observer 或 Integration Module 實作。

## 核心原則

> System Fact 描述事實；Observability 決定如何觀察這些事實。

`Module.Verification.SystemFact.Observability` 不應改變業務流程，也不應讓 Observer failure 影響主要系統執行。
