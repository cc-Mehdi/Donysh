using Donysh.Sms;

const string mellat = "حساب5555555555\nبرداشت50,000,000\nمانده111,111,111\n05/06/15-12:14";
const string blu = "بلو\nبرداشت پول\nمهدی عزیز، 10,000,000 ریال از حساب شما پرید.\nموجودی: 11,111,111 ریال\n12:18\n1405.06.15";
int failed = 0;
void Test(string name, Action body) { try { body(); Console.WriteLine($"PASS {name}"); } catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); } }
void Check(bool value) { if (!value) throw new Exception("Unexpected result"); }
Test("Mellat withdrawal not balance, rial converted, short Persian date", () => {
    var p = BankSmsParser.Parse(mellat);
    Check(p is { Bank: "mellat", Account: "5555555555", AmountToman: 5_000_000 });
    Check(p!.LocalDateTime == new DateTime(2026, 9, 6, 12, 14, 0));
});
Test("Blu withdrawal not balance, Persian full date", () => {
    var p = BankSmsParser.Parse(blu);
    Check(p is { Bank: "blu", Account: null, AmountToman: 1_000_000 });
    Check(p!.LocalDateTime == new DateTime(2026, 9, 6, 12, 18, 0));
});
Test("Persian digits and grouping", () => Check(BankSmsParser.Parse(mellat.Replace("50,000,000", "۵۰٬۰۰۰٬۰۰۰"))?.AmountToman == 5_000_000));
Test("Fully Persian Mellat numbers with Arabic comma grouping", () => {
    const string message = "حساب۵۵۵۵۵۵۵۵۵۵\nبرداشت۵۰،۰۰۰،۰۰۰\nمانده۱۱۱،۱۱۱،۱۱۱\n۰۵/۰۶/۱۵-۱۲:۱۴";
    var parsed = BankSmsParser.Parse(message);
    Check(parsed is { Bank: "mellat", Account: "5555555555", AmountToman: 5_000_000 });
    Check(parsed!.LocalDateTime == new DateTime(2026, 9, 6, 12, 14, 0));
});
Test("Deposits and OTP never create expenses", () => {
    Check(BankSmsParser.Parse(mellat.Replace("برداشت", "واریز")) is null);
    Check(BankSmsParser.Parse("رمز یکبار مصرف\n" + mellat) is null);
    Check(BankSmsParser.Parse("تبلیغات برداشت50,000,000") is null);
});
Test("Ambiguous duplicate withdrawals rejected", () => Check(BankSmsParser.Parse(mellat + "\nبرداشت1,000") is null));
Test("Invalid dates, negative, fractional toman, missing date rejected", () => {
    foreach (var invalid in new[] { mellat.Replace("05/06/15", "05/13/15"), mellat.Replace("50,000,000", "-100"), mellat.Replace("50,000,000", "101"), mellat.Replace("05/06/15-12:14", "") })
        Check(BankSmsParser.Parse(invalid) is null);
});
Test("Malformed grouping and zero rejected", () => {
    Check(BankSmsParser.Parse(mellat.Replace("50,000,000", "50,00,000")) is null);
    Check(BankSmsParser.Parse(mellat.Replace("50,000,000", "0")) is null);
});
Test("Oversized input rejected", () => Check(BankSmsParser.Parse(new string('x', 3000) + mellat) is null));
Test("Receipt retry stable, distinct messages stay distinct", () => {
    var e = new SmsEnvelope("bank", mellat, 1788696000000);
    Check(e.Key == (e with { }).Key);
    Check(e.Key != (e with { SentAtUnixMs = 1788696060000 }).Key);
});
Test("Backoff paces, grows, caps and obeys Retry-After", () => {
    Check(SyncPolicy.Delay(0).TotalSeconds >= 30);
    Check(SyncPolicy.Delay(4) > SyncPolicy.Delay(3));
    Check(SyncPolicy.Delay(30) == TimeSpan.FromMinutes(30));
    Check(SyncPolicy.Delay(0, TimeSpan.FromMinutes(10)) >= TimeSpan.FromMinutes(10));
});
Test("Payload guard rejects oversized and future SMS", () => {
    var now = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
    Check(!SyncPolicy.Valid(new("bank", new string('x', 2049), now.ToUnixTimeMilliseconds()), now));
    Check(!SyncPolicy.Valid(new("bank", mellat, now.AddDays(1).ToUnixTimeMilliseconds()), now));
});
Test("Durable queue does not duplicate after reopening", () => {
    var dir = Path.Combine(Path.GetTempPath(), "donysh-test-" + Guid.NewGuid());
    Directory.CreateDirectory(dir);
    try {
        var path = Path.Combine(dir, "queue.json"); var now = DateTimeOffset.UtcNow;
        var sms = new SmsEnvelope("bank", mellat, now.ToUnixTimeMilliseconds());
        Check(new Outbox(path).Enqueue(sms));
        Check(!new Outbox(path).Enqueue(sms));
        Check(new Outbox(path).Take(now).Count == 1);
    } finally { Directory.Delete(dir, true); }
});
Test("Queue batch limit, persistent pacing, lost acknowledgement and retry", () => {
    var dir = Path.Combine(Path.GetTempPath(), "donysh-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        var path = Path.Combine(dir, "queue.json"); var q = new Outbox(path); var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 12; i++) q.Enqueue(new("bank", mellat, now.ToUnixTimeMilliseconds() + i));
        var batch = q.Take(now); Check(batch.Count == 5);
        Check(new Outbox(path).Take(now.AddSeconds(1)).Count == 0);
        var retried = new Outbox(path).Take(now.AddMinutes(1)); Check(retried.Select(x => x.Key).SequenceEqual(batch.Select(x => x.Key)));
        q.Complete(retried.Select(x => new SmsResult(x.Key, "created")), now.AddMinutes(1));
        Check(q.Take(now.AddMinutes(1)).Count == 0);
        Check(q.Take(now.AddMinutes(2)).All(x => !batch.Any(y => y.Key == x.Key)));
        q.Fail(now.AddMinutes(2), TimeSpan.FromMinutes(10));
        Check(new Outbox(path).Take(now.AddMinutes(3)).Count == 0);
    } finally { Directory.Delete(dir, true); }
});
Test("Blu negative or malformed amount cannot match a valid suffix", () => {
    Check(BankSmsParser.Parse(blu.Replace("10,000,000", "-10,000,000")) is null);
    Check(BankSmsParser.Parse(blu.Replace("10,000,000", "10,00,000")) is null);
});
Test("Long Unicode messages fit the server body cap", () => {
    var dir = Path.Combine(Path.GetTempPath(), "donysh-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        var q = new Outbox(Path.Combine(dir, "queue.json")); var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++) q.Enqueue(new("bank", new string('م', 2048), now.ToUnixTimeMilliseconds() + i));
        var batch = q.Take(now); Check(batch.Count > 0);
        Check(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new SmsBatch(batch)).Length <= 32768);
    } finally { Directory.Delete(dir, true); }
});
Test("Held messages never sent, rejected items held, duplicate ACK removes pending", () => {
    var dir = Path.Combine(Path.GetTempPath(), "donysh-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        var q = new Outbox(Path.Combine(dir, "queue.json")); var now = DateTimeOffset.UtcNow;
        var held = new SmsEnvelope("bank", "برداشت مبهم", now.ToUnixTimeMilliseconds());
        var valid = new SmsEnvelope("bank", mellat, now.ToUnixTimeMilliseconds());
        q.Enqueue(held, true); q.Enqueue(valid);
        Check(q.Take(now).Single().Key == valid.Key);
        q.Complete([new(valid.Key, "rejected")], now);
        Check(q.Take(now.AddMinutes(1)).Count == 0);
        q.Complete([new(valid.Key, "duplicate")], now.AddMinutes(1));
        Check(q.Snapshot().Items.Count == 1);
        Check(!q.Enqueue(valid));
    } finally { Directory.Delete(dir, true); }
});
Test("Corrupt queue is not silently erased", () => {
    var dir = Path.Combine(Path.GetTempPath(), "donysh-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        var path = Path.Combine(dir, "queue.json"); File.WriteAllText(path, "broken"); bool threw = false;
        try { new Outbox(path).Enqueue(new("bank", mellat, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())); }
        catch (System.Text.Json.JsonException) { threw = true; }
        Check(threw); Check(File.ReadAllText(path) == "broken");
    } finally { Directory.Delete(dir, true); }
});
Test("Full queue preserves existing items and reports overflow", () => {
    var dir = Path.Combine(Path.GetTempPath(), "donysh-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
    try {
        var path = Path.Combine(dir, "queue.json"); var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seed = new OutboxState { Items = Enumerable.Range(0, 1000).Select(i => new QueuedSms(new("bank", mellat, now + i), false)).ToList() };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(seed));
        var q = new Outbox(path); Check(!q.Enqueue(new("bank", mellat, now + 2000)));
        Check(q.Snapshot().Items.Count == 1000); Check(q.Snapshot().Overflow == 1);
    } finally { Directory.Delete(dir, true); }
});
Test("Desktop preview parses both samples without requiring a device or server", () => {
    Check(SmsPreview.Describe(SmsPreview.MellatSample).Contains("5,000,000 تومان"));
    Check(SmsPreview.Describe(SmsPreview.BluSample).Contains("1,000,000 تومان"));
    Check(SmsPreview.Describe("رمز پویا 1234").Contains("تشخیص داده نشد"));
});
return failed == 0 ? 0 : 1;
