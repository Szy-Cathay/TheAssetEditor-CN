namespace GameWorld.Core.Vfx;

// The real seeded runtime supplies every new value. Checkpoints share recorded calls, not Random internals.
internal sealed class VfxSeekRandomBudget(long limit)
{
    internal long UsedBytes { get; private set; }
    internal bool Enabled { get; private set; } = true;

    internal bool Reserve(long bytes)
    {
        if (!Enabled || bytes > limit - UsedBytes) { Enabled = false; return false; }
        UsedBytes += bytes;
        return true;
    }

    internal void Disable() => Enabled = false;
}

internal sealed class VfxSeekRandom : Random
{
    private enum Operation : byte { Next, Maximum, Range, Single, Double, Long, LongMaximum, LongRange }
    private readonly record struct Call(Operation Kind, long First, long Second, long Bits);
    private Ledger? _ledger;
    private Random? _direct;
    private int _cursor;

    private VfxSeekRandom(Ledger ledger, int cursor = 0) : base(0) { _ledger = ledger; _cursor = cursor; }
    internal bool CanClone => _ledger?.Budget.Enabled == true && _direct == null;

    internal static Random Create(int seed, VfxSeekRandomBudget budget)
        => Ledger.Create(seed, budget) is { } ledger ? new VfxSeekRandom(ledger) : new Random(seed);

    internal VfxSeekRandom CloneCursor()
    {
        if (!CanClone) throw new InvalidOperationException("An unrecorded random stream cannot be checkpointed.");
        return new(_ledger!, _cursor);
    }

    internal Random DetachExact() => Detach();

    public override int Next() => (int)Read(Operation.Next);
    public override int Next(int maxValue) => (int)Read(Operation.Maximum, maxValue);
    public override int Next(int minValue, int maxValue) => (int)Read(Operation.Range, minValue, maxValue);
    public override float NextSingle() => BitConverter.Int32BitsToSingle((int)Read(Operation.Single));
    public override double NextDouble() => BitConverter.Int64BitsToDouble(Read(Operation.Double));
    public override long NextInt64() => Read(Operation.Long);
    public override long NextInt64(long maxValue) => Read(Operation.LongMaximum, maxValue);
    public override long NextInt64(long minValue, long maxValue) => Read(Operation.LongRange, minValue, maxValue);
    protected override double Sample() => NextDouble();

    public override void NextBytes(byte[] buffer) => Detach().NextBytes(buffer);
    public override void NextBytes(Span<byte> buffer) => Detach().NextBytes(buffer);

    private long Read(Operation operation, long first = 0, long second = 0)
    {
        var call = new Call(operation, first, second, 0);
        if (_direct != null) return Evaluate(_direct, call);
        var ledger = _ledger!;
        if (_cursor < ledger.Count)
        {
            var previous = ledger.At(_cursor);
            if (previous.Kind == operation && previous.First == first && previous.Second == second)
            {
                _cursor++;
                return previous.Bits;
            }
            var generator = ledger.ReplayPrefix(_cursor);
            var branch = ledger.Fork(_cursor, generator);
            if (branch == null)
            {
                _direct = generator; _ledger = null;
                return Evaluate(generator, call);
            }
            _ledger = ledger = branch;
        }
        var bits = Evaluate(ledger.Generator, call);
        if (ledger.Append(call with { Bits = bits })) _cursor++;
        else { _direct = ledger.Generator; _ledger = null; }
        return bits;
    }

    private Random Detach()
    {
        if (_direct != null) return _direct;
        var ledger = _ledger!;
        ledger.Budget.Disable();
        _direct = _cursor == ledger.Count ? ledger.Generator : ledger.ReplayPrefix(_cursor);
        _ledger = null;
        return _direct;
    }

    private static long Evaluate(Random random, Call call) => call.Kind switch
    {
        Operation.Next => random.Next(),
        Operation.Maximum => random.Next((int)call.First),
        Operation.Range => random.Next((int)call.First, (int)call.Second),
        Operation.Single => BitConverter.SingleToInt32Bits(random.NextSingle()),
        Operation.Double => BitConverter.DoubleToInt64Bits(random.NextDouble()),
        Operation.Long => random.NextInt64(),
        Operation.LongMaximum => random.NextInt64(call.First),
        Operation.LongRange => random.NextInt64(call.First, call.Second),
        _ => throw new InvalidOperationException("Unknown random call.")
    };

    private sealed class Ledger(int seed, VfxSeekRandomBudget budget, Random generator)
    {
        private const int BlockLength = 128;
        private readonly List<Call[]> _blocks = [];
        internal VfxSeekRandomBudget Budget { get; } = budget;
        internal Random Generator { get; } = generator;
        internal int Count { get; private set; }

        internal static Ledger? Create(int seed, VfxSeekRandomBudget budget)
            => budget.Reserve(768) ? new(seed, budget, new Random(seed)) : null;

        internal Call At(int index) => _blocks[index / BlockLength][index % BlockLength];

        internal bool Append(Call call)
        {
            if (!Budget.Enabled) return false;
            if (Count % BlockLength == 0)
            {
                // 32 bytes per call plus conservative array/list allocation overhead.
                if (!Budget.Reserve(BlockLength * 32L + 128)) return false;
                _blocks.Add(new Call[BlockLength]);
            }
            _blocks[Count / BlockLength][Count % BlockLength] = call;
            Count++;
            return true;
        }

        internal Random ReplayPrefix(int cursor)
        {
            var random = new Random(seed);
            for (var index = 0; index < cursor; index++) Evaluate(random, At(index));
            return random;
        }

        internal Ledger? Fork(int cursor, Random generatorAtCursor)
        {
            if (!Budget.Reserve(768)) return null;
            var branch = new Ledger(seed, Budget, generatorAtCursor);
            for (var index = 0; index < cursor; index++)
                if (!branch.Append(At(index))) return null;
            return branch;
        }
    }
}
