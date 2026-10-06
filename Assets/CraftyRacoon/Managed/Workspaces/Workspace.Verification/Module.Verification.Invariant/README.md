# module.verification.invariant

純 C# 規則評估機制，無 Unity、simulation、testability framework 依賴。

- `src/API/IInvariant<T>`：對 committed context 評估，回傳明確的 `InvariantResult`；違規內容由 `InvariantViolation` 表示。
- `src/Contract`：穩定 code 與診斷 detail。
- `src/Runtime/InvariantRegistry<T>`：重複檢查、Seal、ordinal code 排序、唯讀結果。
- `tests`：鎖定、順序、結果副本與 exception 行為。

規則的 Code 必須在註冊後保持不變；Evaluate 不應修改被觀察的 gameplay。
Seal 後不可增加規則，Seal 前不可 Evaluate。例外直接交給 caller，不偽裝成一般 invariant violation。
Module 不定義健康值、角色生命週期或任何遊戲規則；不決定評估時機、Session Faulted 政策、trace 或 UI。

正式 namespace 為 `Module.Verification.Invariant`。Invariant 不依賴 Oracle、不保存 history，也不直接成為 SystemFact；需要發布時由 host adapter 轉換。
InvariantViolation 保留舊 serialization contract identity；JSON 的 code/detail 欄位不變。
此 public contract 已 freeze。
