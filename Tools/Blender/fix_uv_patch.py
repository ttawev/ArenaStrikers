# Переносит треугольники руки, которые попадают на белое пятно (перекрытие UV с футболкой),
# в свободное место текстуры с исправленной (закрашенной оранжевым) копией.
import json,struct,io,sys,numpy as np,cv2
src,dst=sys.argv[1],sys.argv[2]   # python fix_uv_patch.py вход.glb выход.glb [условие зоны] [порог насыщенности]
raw=open(src,'rb').read();jl=struct.unpack('<I',raw[12:16])[0]
g=json.loads(raw[20:20+jl]);b=bytearray(raw[20+jl+8:])
def view(i): bv=g['bufferViews'][i];o=bv.get('byteOffset',0);return bytes(b[o:o+bv['byteLength']])
P=np.frombuffer(view(0),np.float32).reshape(-1,3).copy();N=np.frombuffer(view(1),np.float32).reshape(-1,3).copy()
UV=np.frombuffer(view(2),np.float32).reshape(-1,2).copy();IX=np.frombuffer(view(3),np.uint16).reshape(-1,3).astype(np.int64)
imgs=[cv2.imdecode(np.frombuffer(view(g['images'][k]['bufferView']),np.uint8),cv2.IMREAD_UNCHANGED) for k in range(3)]
alb=imgs[1];H,W=alb.shape[:2]
# координаты как в rig_hero: Blender (x,-z,y), нормализация
V=np.c_[P[:,0],-P[:,2],P[:,1]];mn,mx=V.min(0),V.max(0);V=(V-[(mn[0]+mx[0])/2,(mn[1]+mx[1])/2,mn[2]])*1.8/(mx[2]-mn[2])
c=V[IX].mean(1)
# зона «чистой» части (по умолчанию — руки Мига); можно передать своё условие третьим аргументом, c = центры треугольников (x,y,z)
REGION=sys.argv[3] if len(sys.argv)>3 else '(abs(c[:,0])>0.31)&(c[:,2]>0.58)&(c[:,2]<1.2)'
arm=eval(REGION)
def fp(t):
    m=np.zeros((H,W),np.uint8);cv2.fillConvexPoly(m,np.round(np.c_[UV[t,0]*W,UV[t,1]*H]).astype(np.int32),255);return m
hsv=cv2.cvtColor(alb[:,:,:3],cv2.COLOR_BGR2HSV)
SMAX=int(sys.argv[4]) if len(sys.argv)>4 else 110   # «белое»: насыщенность (0..255) ниже этого
white=(hsv[:,:,1]<SMAX)&(hsv[:,:,2]>120)
armm=np.zeros((H,W),np.uint8);used=np.zeros((H,W),np.uint8)
for k,t in enumerate(IX):
    q=np.round(np.c_[UV[t,0]*W,UV[t,1]*H]).astype(np.int32)
    cv2.fillConvexPoly(used,q,255)
    if arm[k]: cv2.fillConvexPoly(armm,q,255)
bad=white&(armm>0)
n_,lab,st,_=cv2.connectedComponentsWithStats(bad.astype(np.uint8))
bad=np.isin(lab,[i for i in range(1,n_) if st[i,4]>=10])
print('пятна',[tuple(st[i]) for i in range(1,n_) if st[i,4]>=10])
T=[k for k in np.where(arm)[0] if (bad&(fp(IX[k])>0)).any()]
# расширим на соседей (1 кольцо), чтобы шов был на чисто оранжевом
def ext(k): q=np.c_[UV[IX[k],0]*W,UV[IX[k],1]*H];return (q.max(0)-q.min(0)).max()
print('размеры', sorted(int(ext(k)) for k in T)[-8:])
T=[k for k in T if ext(k)<60]
vs=set(IX[T].ravel());T=sorted(set(T)|{k for k in np.where(arm)[0] if set(IX[k])&vs and ext(k)<60})
print('треугольников перенесено',len(T))
tv=sorted(set(IX[T].ravel()))
pu=np.c_[UV[tv,0]*W,UV[tv,1]*H];x0,y0=np.floor(pu.min(0)).astype(int)-6;x1,y1=np.ceil(pu.max(0)).astype(int)+6
pw,ph=x1-x0,y1-y0;print('заплатка',pw,ph)
# свободное место: не используется ни одним треугольником (с запасом)
free=(cv2.dilate(used,np.ones((9,9),np.uint8))==0).astype(np.uint8)
S=cv2.integral(free,sdepth=cv2.CV_32S);best=None
for sc in (1.0,0.5,0.34,0.25):
    qw,qh=int(np.ceil(pw*sc))+2,int(np.ceil(ph*sc))+2
    for yy in range(0,H-qh,2):
        for xx in range(0,W-qw,2):
            if S[yy+qh,xx+qw]-S[yy,xx+qw]-S[yy+qh,xx]+S[yy,xx]==qw*qh: best=(xx+1,yy+1);break
        if best: break
    if best: break
assert best,'нет свободного места';bx,by=best;print('место',best,'масштаб',sc)
patch_arm=armm[y0:y1,x0:x1]>0;patch_white=cv2.dilate(white[y0:y1,x0:x1].astype(np.uint8),np.ones((7,7),np.uint8))>0   # с каймой: края пятна тоже закрашиваем
known=patch_arm&~patch_white
fill=(~known).astype(np.uint8)*255
sw,sh=max(1,int(round(pw*sc))),max(1,int(round(ph*sc)))
for k in range(3):
    im=imgs[k];pt=im[y0:y1,x0:x1].copy()
    if pt.ndim==3 and pt.shape[2]==4: pt[:,:,:3]=cv2.inpaint(np.ascontiguousarray(pt[:,:,:3]),fill,5,cv2.INPAINT_TELEA)
    else: pt=cv2.inpaint(pt,fill,5,cv2.INPAINT_TELEA)
    pt=cv2.resize(pt,(sw,sh),interpolation=cv2.INTER_AREA)
    im[by:by+sh,bx:bx+sw]=pt
# новые вершины для перенесённых треугольников
remap={}
P2=list(P);N2=list(N);UV2=list(UV)
for v in tv:
    remap[v]=len(P2);P2.append(P[v]);N2.append(N[v]);UV2.append(np.array([(bx+(UV[v,0]*W-x0)*sc)/W,(by+(UV[v,1]*H-y0)*sc)/H],np.float32))
for k in T: IX[k]=[remap[v] for v in IX[k]]
P2=np.array(P2,np.float32);N2=np.array(N2,np.float32);UV2=np.array(UV2,np.float32)
assert len(P2)<65535
cv2.imwrite('patched_albedo.png',imgs[1])
# пересобрать буфер
chunks=[P2.tobytes(),N2.tobytes(),UV2.tobytes(),IX.astype(np.uint16).tobytes()]+[cv2.imencode('.png',imgs[k])[1].tobytes() for k in range(3)]
nb=bytearray();
for i,ch in enumerate(chunks):
    while len(nb)%4: nb+=b'\0'
    g['bufferViews'][i]['byteOffset']=len(nb);g['bufferViews'][i]['byteLength']=len(ch);nb+=ch
while len(nb)%4: nb+=b'\0'
g['buffers'][0]['byteLength']=len(nb)
for i in range(3): g['accessors'][i]['count']=len(P2)
g['accessors'][3]['count']=IX.size
js=json.dumps(g,separators=(',',':')).encode();js+=b' '*((4-len(js)%4)%4)
out=struct.pack('<III',0x46546C67,2,12+8+len(js)+8+len(nb))+struct.pack('<II',len(js),0x4E4F534A)+js+struct.pack('<II',len(nb),0x004E4942)+bytes(nb)
open(dst,'wb').write(out);print('ok',len(out))
