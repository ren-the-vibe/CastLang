-- Lua 5.4: deterministic benchmark arena. Must match the C# invariant.
local SIZE, HCEIL, CAP = 20, 32, 2000

local function run(n0, ticks, seed)
    local s = seed & 0xFFFFFFFF
    local function nxt() s = (1664525 * s + 1013904223) & 0xFFFFFFFF; return s end
    local function rint(n) return nxt() % n end
    local function rspan() return (nxt() % 3) - 1 end

    local px, pz, ph = {}, {}, {}
    local vx, vz = {}, {}
    local lineage, ancestors = {}, {}
    local alive, isMage, cursed, oob = {}, {}, {}, {}

    local N, births, deaths, cursings, live, nextL, cursedL, mage = 0, 0, 0, 0, 0, 1, -1, 0

    local function contains(t, x)
        for k = 1, #t do if t[k] == x then return true end end
        return false
    end

    local function build()
        N, nextL, births, deaths, cursings, cursedL = 0, 1, 0, 0, 0, -1
        for _ = 1, n0 do
            local i = N; N = N + 1
            px[i] = 2 + rint(SIZE - 4); pz[i] = 2 + rint(SIZE - 4); ph[i] = HCEIL // 2
            vx[i] = rspan(); vz[i] = rspan()
            lineage[i] = nextL; nextL = nextL + 1
            ancestors[i] = { lineage[i] }
            alive[i] = true; isMage[i] = false; cursed[i] = false; oob[i] = false
        end
        mage = N; N = N + 1
        px[mage] = SIZE // 2; pz[mage] = SIZE // 2; ph[mage] = HCEIL // 2
        vx[mage] = rspan(); vz[mage] = rspan()
        lineage[mage] = nextL; nextL = nextL + 1
        ancestors[mage] = { lineage[mage] }
        alive[mage] = true; isMage[mage] = true; cursed[mage] = false; oob[mage] = false
        live = N
    end

    local function oobCheck(i)
        return px[i] < 0 or px[i] >= SIZE or pz[i] < 0 or pz[i] >= SIZE or ph[i] > HCEIL
    end

    local function ruleKillOob()
        for i = 0, N - 1 do
            if alive[i] and oob[i] and not isMage[i] then alive[i] = false; deaths = deaths + 1; live = live - 1 end
        end
    end
    local function ruleBirth(p1, p2)
        if live > 400 then return end
        if N >= CAP then return end
        local c = N; N = N + 1
        px[c] = (px[p1] + px[p2]) / 2; pz[c] = (pz[p1] + pz[p2]) / 2; ph[c] = HCEIL // 2
        vx[c] = rspan(); vz[c] = rspan()
        lineage[c] = nextL; nextL = nextL + 1
        local anc = { lineage[c] }
        for k = 1, #ancestors[p1] do if not contains(anc, ancestors[p1][k]) then anc[#anc+1] = ancestors[p1][k] end end
        for k = 1, #ancestors[p2] do if not contains(anc, ancestors[p2][k]) then anc[#anc+1] = ancestors[p2][k] end end
        ancestors[c] = anc
        alive[c] = true; isMage[c] = false; oob[c] = false
        cursed[c] = (cursedL >= 0 and contains(anc, cursedL))
        births = births + 1; live = live + 1
    end
    local function ruleCurse(off)
        cursings = cursings + 1; cursedL = off
        px[mage] = SIZE // 2; pz[mage] = SIZE // 2; ph[mage] = HCEIL // 2
        for i = 0, N - 1 do
            if alive[i] and contains(ancestors[i], off) then cursed[i] = true end
        end
    end
    local function ruleStandingCurse()
        if cursedL < 0 then return end
        for i = 0, N - 1 do
            if alive[i] and not cursed[i] and contains(ancestors[i], cursedL) then cursed[i] = true end
        end
    end

    local function step()
        local mid = SIZE // 2
        for i = 0, N - 1 do
            if alive[i] then
                local sx, sz = rspan(), rspan()
                if px[i] < mid - 6 then sx = sx + 1 elseif px[i] > mid + 6 then sx = sx - 1 end
                if pz[i] < mid - 6 then sz = sz + 1 elseif pz[i] > mid + 6 then sz = sz - 1 end
                vx[i] = vx[i] + sx; vz[i] = vz[i] + sz
                if vx[i] > 1 then vx[i] = 1 end; if vx[i] < -1 then vx[i] = -1 end
                if vz[i] > 1 then vz[i] = 1 end; if vz[i] < -1 then vz[i] = -1 end
                px[i] = px[i] + vx[i]; pz[i] = pz[i] + vz[i]
            end
        end
        for i = 0, N - 1 do
            if alive[i] and oobCheck(i) then
                if isMage[i] then
                    px[i] = math.min(math.max(px[i], 1), SIZE - 2)
                    pz[i] = math.min(math.max(pz[i], 1), SIZE - 2)
                else
                    oob[i] = true
                end
            end
        end
        local count = N
        for i = 0, count - 1 do
            if alive[i] and not oob[i] then
                for j = i + 1, count - 1 do
                    if alive[j] and not oob[j] and px[i] == px[j] and pz[i] == pz[j] then
                        local dirx = (px[j] >= mid) and 1 or -1
                        local dirz = (pz[j] >= mid) and 1 or -1
                        px[j] = px[j] + dirx * 2; pz[j] = pz[j] + dirz * 2
                        if isMage[i] and oobCheck(i) then ruleCurse(lineage[j])
                        elseif isMage[j] and oobCheck(j) then ruleCurse(lineage[i])
                        elseif not isMage[i] and not isMage[j] then ruleBirth(i, j) end
                    end
                end
            end
        end
    end

    build()
    for _ = 1, ticks do
        step(); ruleKillOob(); ruleStandingCurse()
        for i = 0, N - 1 do oob[i] = false end
    end
    local chk = 0
    for i = 0, N - 1 do
        if alive[i] then
            chk = chk + px[i] * 3 + pz[i] * 5 + ph[i] * 7 + lineage[i] * 11 + (cursed[i] and 13 or 0)
        end
    end
    return births, deaths, cursings, live, chk
end

local N0 = tonumber(arg[1]) or 30
local Ticks = tonumber(arg[2]) or 300
local seed = tonumber(arg[3]) or 12345
run(N0, 10, seed) -- warmup
local t0 = os.clock()
local b, d, c, l, chk = run(N0, Ticks, seed)
local el = os.clock() - t0
print(string.format("lua\t%d\t%d\t%.1f\t%d\t%d\t%d\t%d\t%.0f", N0, Ticks, el * 1000, b, d, c, l, chk))
