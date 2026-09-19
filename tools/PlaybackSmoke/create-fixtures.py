"""Generate local H.264/AAC fixtures using a full, development-only FFmpeg build."""
import argparse
import json
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True)
parser.add_argument('--output', default='artifacts/native-playback-test')
parser.add_argument('--port', type=int, default=18765)
args = parser.parse_args()
root = Path(args.output).resolve()
root.mkdir(parents=True, exist_ok=True)


def run(*arguments):
    subprocess.run([args.ffmpeg, '-v', 'error', '-y', *arguments], cwd=root, check=True)


run('-f', 'lavfi', '-i', 'testsrc=size=640x360:rate=24',
    '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000',
    '-f', 'lavfi', '-i', 'sine=frequency=880:sample_rate=48000',
    '-t', '8', '-map', '0:v', '-map', '1:a', '-map', '2:a',
    '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-g', '48', '-c:a', 'aac',
    '-metadata:s:a:0', 'language=jpn', '-metadata:s:a:1', 'language=bel', 'input.mp4')
(root / 'dialogue.ass').write_text('''[Script Info]
ScriptType: v4.00+
PlayResX: 640
PlayResY: 360
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,32,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:08.00,Default,,0,0,0,,Native + libass test · Субцітры
''', encoding='utf-8')
(root / 'fallback.ass').write_text((root / 'dialogue.ass').read_text(encoding='utf-8').replace('Style: Default,Arial,', 'Style: Default,Anibel Missing Test Font,'), encoding='utf-8')
font = str(Path(os.environ['WINDIR']) / 'Fonts' / 'arial.ttf')
run('-i', 'input.mp4', '-i', 'dialogue.ass', '-map', '0', '-map', '1', '-c', 'copy',
    '-attach', font, '-metadata:s:t:0', 'mimetype=application/x-truetype-font',
    '-metadata:s:s:0', 'title=Субцітры', 'download.mkv')
run('-i', 'input.mp4', '-map', '0:a:0', '-c', 'copy', 'audio.m4a')
run('-i', 'input.mp4', '-map', '0:v', '-map', '0:a:0', '-c', 'copy',
    '-hls_time', '2', '-hls_list_size', '0', 'stream.m3u8')
run('-i', 'input.mp4', '-map', '0:v', '-map', '0:a:0', '-vf', 'scale=320:180',
    '-c:v', 'libx264', '-g', '48', '-c:a', 'copy', '-hls_time', '2', '-hls_list_size', '0', 'low.m3u8')
(root / 'master.m3u8').write_text('#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=320x180\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=640x360\nstream.m3u8\n', encoding='utf-8')
run('-i', 'input.mp4', '-map', '0:v', '-map', '0:a:0', '-c', 'copy',
    '-seg_duration', '2', '-adaptation_sets', 'id=0,streams=v id=1,streams=a', 'stream.mpd')
(root / 'sources.json').write_text(json.dumps([
    str(root / 'input.mp4'), f'http://127.0.0.1:{args.port}/stream.m3u8',
    f'http://127.0.0.1:{args.port}/master.m3u8', f'http://127.0.0.1:{args.port}/stream.mpd',
    'separate-audio', str(root / 'download.mkv'), '\\\\?\\' + str(root / 'download.mkv')
]), encoding='utf-8')
print(root)
