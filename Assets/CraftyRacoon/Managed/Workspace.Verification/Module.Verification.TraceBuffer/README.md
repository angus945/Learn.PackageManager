# module.verification.trace-buffer

純 C#、不依賴 Unity／simulation／其他 verification capability 的 generic temporal storage。
`src/API` 為讀寫 ports、`src/Contract` 為 cursor／read state、`src/Runtime` 為 buffer，測試在 `tests`。

```csharp
ITraceBuffer<string> buffer = new TraceBuffer<string>(512);
ITraceWriter<string> writer = buffer.Writer;
ITraceReader<string> reader = buffer.Reader;
writer.Append("started");
TraceRead<string> page = reader.Read(default, 64);
TraceCursor cursor = page.NextCursor;
TraceRead<string> next = reader.Read(cursor, 64);
```

## API／Contract

- `ITraceBuffer<T>` 提供 append、ordered read、cursor、pagination、eviction 的 bounded storage contract；owner 也可分別交付 Writer／Reader facade。
- 紀錄 Sequence 從 1 遞增，是 journal 位置，不是遊戲 Action sequence、tick 或 wall clock。
- Cursor = StreamId + AfterSequence，採 exclusive 語意。default 表示從目前保留的最早紀錄讀起。
- Read 不消耗來源、不更動其他 reader。多個工具各自持有 cursor。
- 同 stream 的未來 cursor／非正數 maxItems 被拒絕；讀取量最多為來源容量。
- MissedCount 是這次 cursor 尚未讀到但已被覆蓋的筆數；使用 NextCursor 不會重複計入。
- OverwrittenCount 是來源自建立以來累計被覆蓋筆數，不等於某個 reader 遺失筆數。
- `TraceReadState.ForeignCursor` 表示 cursor 屬於另一個 stream。回傳新 stream 最早保留資料，consumer 必須清除舊 stream 的 local history。
- Foreign cursor 無法量化舊 stream 最後尚未讀取的資料；此時 MissedCount 僅描述新 stream 已被覆蓋的前綴。
- HasMore 表示還有未讀的保留紀錄；NextCursor 是本頁最後已讀位置。
- Sequence 耗盡明確失敗，不 wrap；新 buffer 產生新 stream identity，沒有跨 stream 重用序號的混淆。
- Read 擁有自己的唯讀 payload 陣列，但不深拷貝 T。T 必須為不可變 payload。
- 單執行緒；未提供 thread safety、持久化或 transport。容量限制的是筆數，不是任意 payload 的 byte 數。

Simulation 的 Session／Tick／Wave／Actor 欄位由 adopter 定義在自己的 payload；本 module 不解釋它們，也不知道 facts、operations、diagnostics、oracles、invariants or evidence。此 public contract 已 freeze。
