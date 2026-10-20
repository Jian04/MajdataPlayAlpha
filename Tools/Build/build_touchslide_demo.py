from pathlib import Path
import shutil, math, wave, struct, subprocess
chart=Path('output/Alpha_TouchSlide_Mine_Test');chart.mkdir(parents=True,exist_ok=True)
parts=[('Normal touch arc','A1>A4[1:1],,,,5,,,'),('Mine trail / safe','A1>A4m[1:1],,,,5,,,'),('Mine head / normal trail','A1m>A4[1:1],,,,5,,,'),('Mine head and trail / safe','A1m>A4m[1:1],,,,5,,,'),('Mine trail / forced contact',None),('Mine straight','A1-A5m[1:1],,,,7,,,'),('Normal touch spiral','B1>B5[1:1],,,,7,,,'),('Mine touch spiral','B1>B5m[1:1],,,,7,,,'),('Normal key slide','1-5[1:1],,,,7,,,'),('Mine key slide / safe','1-5m[1:1],,,,7,,,'),('D path / mine','1d-5dm[1:1],,,,7,,,'),('Touch EX / break / mine','A1x>A4b[1:1]/A5m>A8m[1:1],,,,1,,,')]
lines=['&title=Alpha TouchSlide Mine Test','&artist=Procedural click fixture','&des=TouchSlide mine visual regression','&first=3','&lv_5=TEST','&inote_5=','(120){4}']
for i,(title,body) in enumerate(parts):
    lines+= [f'|| {3+i*4:.1f}s - {title}',f'<TEXT*("{title}")>']
    if body is None:
        cells=['']*32
        cells[0]='A1>A4m[1:1]'
        for slot,area in [(4,'A1'),(8,'A2'),(12,'A3'),(16,'A4')]:cells[slot]=area
        lines.append('{16}'+','.join(cells)+',')
        lines.append('{4}')
    else:lines.append(body+',')
lines+= ['<TEXT*("END")>E']
(chart/'maidata.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
rate=44100;duration=52
with wave.open(str(chart/'track.wav'),'wb') as output:
    output.setnchannels(2);output.setsampwidth(2);output.setframerate(rate)
    for second in range(duration):
        frames=[]
        for j in range(rate):
            t=second+j/rate
            dt=(t-3)%0.5
            value=int(9000*math.exp(-dt*90)*math.sin(2*math.pi*(1100 if round((t-3)*2)%8==0 else 650)*dt)) if t>=3 and dt<.08 else 0
            frames.append(struct.pack('<hh',value,value))
        output.writeframes(b''.join(frames))
shutil.copyfile('output/Alpha_All_Syntax_Test/bg.png',chart/'bg.png')
ffmpeg=Path('alpha/.codex-build/ViewBuild/App/MajdataView/MajdataView_Data/StreamingAssets/ffmpeg.exe')
subprocess.run([str(ffmpeg),'-y','-hide_banner','-loglevel','error','-i',str(chart/'track.wav'),
                '-codec:a','libmp3lame','-b:a','192k',str(chart/'track.mp3')],check=True)
(chart/'track.wav').unlink()
(chart/'README.md').write_text('TouchSlide / Mine 对照谱，约 51 秒。每段 4 秒，中央字幕显示测试类别。\n普通 / 轨迹地雷 / 头地雷 / 双地雷、主动触碰地雷、普通与 D 区轨迹分别展示。\n地雷仅按时间消退画面，不主动发送 DJAuto 输入；强制接触段用 A1/A2/A3/A4 验证原生地雷判定。\n',encoding='utf-8')
print('Demo:',chart)
