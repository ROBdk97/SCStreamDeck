using FluentAssertions;
using NAudio.Wave;
using SCStreamDeck.Services.Audio;

namespace Tests.Unit.Services.Audio;

public sealed class AudioReaderAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_DisposesSourceWhenPlaybackCompletes_AndStopsReadingAfterwards(bool useSpan)
    {
        FakeSampleProvider source = new(5, 0, 7);
        AudioReaderAdapter adapter = new(source);
        float[] buffer = new float[16];

        int Read() => useSpan ? adapter.Read(buffer.AsSpan()) : adapter.Read(buffer, 0, buffer.Length);

        Read().Should().Be(5);
        source.DisposeCalls.Should().Be(0);

        Read().Should().Be(0);
        source.DisposeCalls.Should().Be(1);

        // Once disposed, adapter should not read from source again.
        Read().Should().Be(0);
        source.ReadCalls.Should().Be(2);
        source.DisposeCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_WritesOnlyToRequestedBufferSlice(bool useSpan)
    {
        FakeSampleProvider source = new(3);
        AudioReaderAdapter adapter = new(source);
        float[] buffer = [-1, -1, -1, -1, -1];

        int samplesRead = useSpan ? adapter.Read(buffer.AsSpan(1, 3)) : adapter.Read(buffer, 1, 3);

        samplesRead.Should().Be(3);
        buffer.Should().Equal(-1f, 3f, 3f, 3f, -1f);
        source.ReadCalls.Should().Be(1);
        source.DisposeCalls.Should().Be(0);
        adapter.WaveFormat.Should().BeSameAs(source.WaveFormat);
    }

    private sealed class FakeSampleProvider : ISampleProvider, IDisposable
    {
        private readonly int[] _sequence;
        private int _index;

        public FakeSampleProvider(params int[] sequence)
        {
            _sequence = sequence;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        }

        public int ReadCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            ReadCalls++;
            if (_index >= _sequence.Length)
            {
                return 0;
            }

            int value = _sequence[_index++];
            buffer[..value].Fill(value);
            return value;
        }
    }
}
