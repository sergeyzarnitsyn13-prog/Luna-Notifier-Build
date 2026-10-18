# Luna Notify 0.8 — Original MoonGlow v10 port
Source: Luna-MoonGlow-v10-PORTABLE-NOBUILD.zip, `source/Harness.cs` and `source/SpeechBubble.xaml`, archived 2026-09-17.
Copied the exact `PopScale` animation from the original `Harness.cs` and `DisplaySpeech` invocation:
- Discrete scale 0.08 at 0 ms.
- 1.018 at 280 ms, cubic EaseOut.
- 1.0 at 420 ms, cubic EaseInOut.
- Root opacity 0 to 1 in 160 ms, cubic EaseOut.
- Close scale -> 0.82 and opacity -> 0 in 150 ms.

Original panel typography, gradient stops, halo / drop shadow and QuietAction are reused.
Only notifier-specific deviations: tail removed (rounded rectangle geometry); chat buttons ('Позже', 'Открыть чат') removed; fixed text, dismissal and secure loopback test remain; positions in WPF DIPs from working verified version.
Do not reintroduce seed, opacity-mask reveal, spark effects or experimental glow choreography.
This is a *visual test*; not a production private event-delivery solution.
