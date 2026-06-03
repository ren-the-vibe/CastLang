import sys, time

def main():
    N = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    T = int(sys.argv[2]) if len(sys.argv) > 2 else 200

    health = [0.0] * N
    max_health = [0.0] * N
    poisoned = [0.0] * N

    def build():
        for i in range(N):
            mh = 50 + (i % 100)
            max_health[i] = mh
            health[i] = ((i * 37) % int(mh)) + 1
            poisoned[i] = 1.0 if (i % 3 == 0) else 0.0

    state = {"checksum": 0.0, "total_damage": 0.0}

    def tick():
        cs = state["checksum"]; td = state["total_damage"]
        for i in range(N):
            mh = max_health[i]
            if poisoned[i] != 0:
                dmg = max(1.0, min(mh * 0.05, 25.0))   # Clamp(mh*0.05, 1, 25)
                health[i] -= dmg
                if health[i] <= 0:
                    poisoned[i] = 0.0
                    health[i] = 0.0
                td += dmg
            elif health[i] <= mh * 0.30:
                regen = min(mh * 0.02, mh - health[i])
                health[i] += regen
            cs += health[i]
        state["checksum"] = cs; state["total_damage"] = td

    # warmup
    build()
    for _ in range(3):
        tick()
    # timed
    build()
    state["checksum"] = 0.0; state["total_damage"] = 0.0
    t0 = time.perf_counter()
    for _ in range(T):
        tick()
    elapsed = time.perf_counter() - t0

    ops = N * T
    print(f"python\t{N}\t{T}\t{elapsed*1000:.1f}\t{ops/elapsed/1e6:.3f}\t{state['checksum']:.0f}\t{state['total_damage']:.0f}")

if __name__ == "__main__":
    main()
