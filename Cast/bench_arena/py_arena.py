import sys, time

SIZE = 20
HCEIL = 32

def run(n0, ticks, seed):
    s = [seed]
    def nxt():
        s[0] = (1664525 * s[0] + 1013904223) & 0xFFFFFFFF
        return s[0]
    def rint(n): return nxt() % n
    def rspan(): return (nxt() % 3) - 1

    px=[0.0]*2000; pz=[0.0]*2000; ph=[0.0]*2000
    vx=[0]*2000; vz=[0]*2000
    lineage=[0.0]*2000; ancestors=[None]*2000
    alive=[False]*2000; is_mage=[False]*2000; cursed=[False]*2000; oob=[False]*2000

    state = {"N":0,"births":0,"deaths":0,"cursings":0,"live":0,"nextL":1.0,"cursedL":-1.0,"mage":0}

    def build():
        state["N"]=0; state["nextL"]=1.0
        state["births"]=0; state["deaths"]=0; state["cursings"]=0; state["cursedL"]=-1.0
        for _ in range(n0):
            i=state["N"]; state["N"]+=1
            px[i]=2+rint(SIZE-4); pz[i]=2+rint(SIZE-4); ph[i]=HCEIL//2
            vx[i]=rspan(); vz[i]=rspan()
            lineage[i]=state["nextL"]; state["nextL"]+=1
            ancestors[i]=[lineage[i]]
            alive[i]=True; is_mage[i]=False; cursed[i]=False; oob[i]=False
        m=state["N"]; state["N"]+=1; state["mage"]=m
        px[m]=SIZE//2; pz[m]=SIZE//2; ph[m]=HCEIL//2
        vx[m]=rspan(); vz[m]=rspan()
        lineage[m]=state["nextL"]; state["nextL"]+=1
        ancestors[m]=[lineage[m]]
        alive[m]=True; is_mage[m]=True; cursed[m]=False; oob[m]=False
        state["live"]=state["N"]

    def oob_check(i):
        return px[i]<0 or px[i]>=SIZE or pz[i]<0 or pz[i]>=SIZE or ph[i]>HCEIL

    def rule_kill_oob():
        for i in range(state["N"]):
            if alive[i] and oob[i] and not is_mage[i]:
                alive[i]=False; state["deaths"]+=1; state["live"]-=1

    def rule_birth(p1,p2):
        if state["live"]>400: return
        if state["N"]>=2000: return
        c=state["N"]; state["N"]+=1
        px[c]=(px[p1]+px[p2])/2; pz[c]=(pz[p1]+pz[p2])/2; ph[c]=HCEIL//2
        vx[c]=rspan(); vz[c]=rspan()
        lineage[c]=state["nextL"]; state["nextL"]+=1
        anc=[lineage[c]]
        for a in ancestors[p1]:
            if a not in anc: anc.append(a)
        for a in ancestors[p2]:
            if a not in anc: anc.append(a)
        ancestors[c]=anc
        alive[c]=True; is_mage[c]=False; oob[c]=False
        cursed[c]= state["cursedL"]>=0 and state["cursedL"] in anc
        state["births"]+=1; state["live"]+=1

    def rule_curse(off):
        state["cursings"]+=1; state["cursedL"]=off
        m=state["mage"]; px[m]=SIZE//2; pz[m]=SIZE//2; ph[m]=HCEIL//2
        for i in range(state["N"]):
            if alive[i] and off in ancestors[i]: cursed[i]=True

    def rule_standing_curse():
        if state["cursedL"]<0: return
        cl=state["cursedL"]
        for i in range(state["N"]):
            if alive[i] and not cursed[i] and cl in ancestors[i]: cursed[i]=True

    def step():
        mid=SIZE//2; N=state["N"]
        for i in range(N):
            if not alive[i]: continue
            sx=rspan(); sz=rspan()
            if px[i]<mid-6: sx+=1
            elif px[i]>mid+6: sx-=1
            if pz[i]<mid-6: sz+=1
            elif pz[i]>mid+6: sz-=1
            vx[i]+=sx; vz[i]+=sz
            if vx[i]>1: vx[i]=1
            if vx[i]<-1: vx[i]=-1
            if vz[i]>1: vz[i]=1
            if vz[i]<-1: vz[i]=-1
            px[i]+=vx[i]; pz[i]+=vz[i]
        for i in range(N):
            if not alive[i] or not oob_check(i): continue
            if is_mage[i]:
                px[i]=min(max(px[i],1),SIZE-2); pz[i]=min(max(pz[i],1),SIZE-2)
            else:
                oob[i]=True
        for i in range(N):
            if not alive[i] or oob[i]: continue
            for j in range(i+1,N):
                if not alive[j] or oob[j]: continue
                if px[i]!=px[j] or pz[i]!=pz[j]: continue
                dirx=1 if px[j]>=mid else -1
                dirz=1 if pz[j]>=mid else -1
                px[j]+=dirx*2; pz[j]+=dirz*2
                if is_mage[i] and oob_check(i): rule_curse(lineage[j])
                elif is_mage[j] and oob_check(j): rule_curse(lineage[i])
                elif not is_mage[i] and not is_mage[j]: rule_birth(i,j)

    build()
    for _ in range(ticks):
        step(); rule_kill_oob(); rule_standing_curse()
        for i in range(state["N"]): oob[i]=False

    chk=0.0
    for i in range(state["N"]):
        if alive[i]:
            chk += px[i]*3 + pz[i]*5 + ph[i]*7 + lineage[i]*11 + (13 if cursed[i] else 0)
    return state["births"],state["deaths"],state["cursings"],state["live"],chk

def main():
    N0=int(sys.argv[1]) if len(sys.argv)>1 else 30
    Ticks=int(sys.argv[2]) if len(sys.argv)>2 else 300
    seed=int(sys.argv[3]) if len(sys.argv)>3 else 12345
    run(N0,10,seed)  # warmup
    t0=time.perf_counter()
    b,d,c,l,chk=run(N0,Ticks,seed)
    el=time.perf_counter()-t0
    print(f"python\t{N0}\t{Ticks}\t{el*1000:.1f}\t{b}\t{d}\t{c}\t{l}\t{chk:.0f}")

if __name__=="__main__":
    main()
