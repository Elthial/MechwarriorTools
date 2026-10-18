"""Optional private conformance: real native IRQ instructions versus C# tone ticks.

Uses the existing research CPU harness, not a second C# interpreter. Supplies only
the finite DOSBox-X memory profile already recovered by the C17 port. No desktop,
sound device, original asset writes or skipped native countdowns.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import uuid

TOOLING = Path(__file__).resolve().parents[1]
SCRIPTS = Path(__import__('os').environ.get('MECHWARRIOR_RESEARCH_SCRIPTS', str(TOOLING / 'evidence/private/scripts')))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', type=Path, required=True)
    parser.add_argument('--configuration', default='Release')
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('native_sound_parity', SCRIPTS / 'Test-SoundBankArchivePlayback.py')
    native = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(native)
    fixture = native.SoundBankArchivePlaybackTests
    fixture.setUpClass()
    case = fixture()
    cli = TOOLING / f'src/MechwarriorTooling.Cli/bin/{args.configuration}/net10.0/MechwarriorTooling.Cli.dll'
    output = TOOLING / 'artifacts' / ('native-tone-' + uuid.uuid4().hex)
    output.mkdir(parents=True)
    total = 0
    for event in range(15):
        trace_path = output / f'{event:02X}.json'
        subprocess.run(['dotnet', str(cli), 'sounds', 'render', '--bank', str(args.game_dir / 'btechsnd.snd'),
                        '--event', str(event), '--seconds', '5', '--output', str(output / f'{event:02X}.wav'),
                        '--trace', str(trace_path)], check=True, capture_output=True)
        trace = json.loads(trace_path.read_text())
        cpu = case.loaded()
        physical = cpu.bank(0)
        for interrupt, vector in enumerate((0x1D3B1F4A, 0x0070000E, 0x0070000E, 0x0070000E, 0x0070000E, 0xF000FF54)):
            physical.write(interrupt * 4, vector, 4)
        for at in range(0x1F4A, 0x211E):
            physical.write(at, 0, 1)
        case.dispatch(cpu, event)
        tones = cpu.bank(0x2FEB)
        state = tones.read(0x39E)
        for expected in trace:
            ports = case.irq(cpu)
            actual = dict(Period=tones.read(state + 8), Level=tones.read(state + 10),
                          Phase=tones.read(state + 0x1A), PhaseLimit=tones.read(state + 0x2A),
                          ModulationOffset=tones.read(state + 0x2E), Cursor=tones.read(state),
                          DelayTicks=tones.read(state + 2), Active=bool(tones.read(0xDF, 1) & 1))
            for key, value in actual.items():
                if expected[key] != value:
                    raise AssertionError(f'Event {event:02X} tick {expected["Tick"]} {key}: C# {expected[key]}, ASM {value}')
            pit = [entry[2] for entry in ports if entry[:2] == ('out', 0x42)]
            gate = [entry[2] & 3 for entry in ports if entry[:2] == ('out', 0x61)]
            assert pit == [actual['Period'] & 255, actual['Period'] >> 8], (event, expected['Tick'], pit)
            assert gate == [3 if actual['Level'] else 0], (event, expected['Tick'], gate)
            total += 1
            if total % 64 == 0:
                for log in (cpu.events, cpu.calls, cpu.returns, cpu.near_calls, cpu.ports):
                    log.clear()
        print(f'PASS event {event:02X}: {len(trace)} actual native IRQs', flush=True)
    print(f'PASS all 15 events: {total} native/C# tick and PIT/gate comparisons; output {output}')


if __name__ == '__main__':
    main()
