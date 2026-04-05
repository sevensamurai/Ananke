using System.Collections.Concurrent;
using Ananke.StateMachine;

namespace StateMachineDemo;

// -- Trip reporter (the "second process") ----------------------------------
//    Observes FSM transitions and records trip data.
//    In production this would be a separate microservice reading from MQTT.
sealed class TripReporter
{
    private readonly ConcurrentDictionary<string, TripLog> _logs = new();

    public void OnTransition(CarContext ctx, EngineTransition transition, TransitionResult<EngineState> result)
    {
        if (!result.Success) return;

        var log = _logs.GetOrAdd(ctx.Id, _ => new TripLog());

        switch (transition)
        {
            case EngineTransition.Start:
                log.EngineStarted();
                break;
            case EngineTransition.Drive:
                log.TripSegmentStarted();
                break;
            case EngineTransition.Halt:
                log.TripSegmentEnded(simulatedDistanceKm: Random.Shared.Next(5, 50));
                break;
            case EngineTransition.Park:
                log.EngineStopped();
                break;
        }
    }

    public void PrintReport(string carId)
    {
        if (!_logs.TryGetValue(carId, out var log))
        {
            Console.WriteLine($"  No trip data for {carId}");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ?? Trip Report: {carId} ??");
        Console.ResetColor();
        Console.WriteLine($"    Engine sessions:   {log.EngineSessions}");
        Console.WriteLine($"    Trip segments:     {log.TripSegments}");
        Console.WriteLine($"    Total distance:    {log.TotalDistanceKm} km");
        Console.WriteLine($"    Total engine time: {log.TotalEngineTime.TotalSeconds:F1}s");
    }

    private sealed class TripLog
    {
        private DateTime? _engineStart;
        private DateTime? _segmentStart;

        public int EngineSessions { get; private set; }
        public int TripSegments { get; private set; }
        public double TotalDistanceKm { get; private set; }
        public TimeSpan TotalEngineTime { get; private set; }

        public void EngineStarted()
        {
            EngineSessions++;
            _engineStart = DateTime.UtcNow;
        }

        public void TripSegmentStarted() => _segmentStart = DateTime.UtcNow;

        public void TripSegmentEnded(double simulatedDistanceKm)
        {
            TripSegments++;
            TotalDistanceKm += simulatedDistanceKm;
        }

        public void EngineStopped()
        {
            if (_engineStart.HasValue)
            {
                TotalEngineTime += DateTime.UtcNow - _engineStart.Value;
                _engineStart = null;
            }
        }
    }
}

