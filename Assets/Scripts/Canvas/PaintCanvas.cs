using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class PaintCanvas : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    //  واجهة المفتش (Inspector)
    // ──────────────────────────────────────────────────────────────

    // لماذا الاخاديد تمتص الطلاء بشكل اكبر 

    // كيف تؤثر طول الذراع على حجم الفرشاة 

    // شرح استطالة الضربة بشكل مفصل 

    // ما هي ميزة Coroutine

    



    [Header("أبعاد اللوحة")]
    public float CanvasWidth  = 2f;
    public float CanvasHeight = 2f;
    public int   TextureRes   = 1024;

    [Header("خصائص السطح")]
    [Range(0f, 1f)]
    [Tooltip("0 = زجاج (انتشار واسع)، 1 = قماش (حواف حادة)")]
    public float AbsorptionRate = 0.85f;

    [Header("اتجاه اللوحة")]
    [Tooltip("المحور المحلي العمودي للوجه المرسوم\n" +
             "Plane (افتراضي) = Vector3.up\n" +
             "Quad مواجه الكاميرا = Vector3.forward")]
    public Vector3 PaintNormalLocal = Vector3.up;
    public bool AutoCalculateTilt   = true;
    [Range(0f, 180f)]
    public float TiltAngle = 0f;

    [Header("البيئة")]
    [Range(0f, 1f)]
    [Tooltip("0 = جاف (ألوان منفصلة)، 1 = رطب (ألوان تندمج وتسيل)")]
    public float Humidity = 0.5f;

    [Header("الجسيمات")]
    [Tooltip("كتلة مرجعية لمعادلة التحجيم")]
    public float    ReferenceParticleMass = 0.012f;
    [Tooltip("حد أدنى/أعلى لمعامل التحجيم")]
    public Vector2  MassScaleClamp        = new Vector2(0.6f, 2.8f);

    [Header("كشف الاصطدام")]
    [Tooltip("عتبة المسافة من مستوى اللوحة")]
    public float HitThreshold = 0.05f;

    // ── ★ جديد ──────────────────────────────────────────────────
    [Header("قماش اللوحة والفرشاة")]
    [Range(5f, 80f)]
    [Tooltip("مقياس الحبيبات — أكبر = أنماط أكثر تفصيلاً")]
    public float GrainScale = 25f;

    // خصائص ديناميكية تُحسب بناءً على المحاكاة:
    // GrainStrength → driven by canvas absorption
    private float GrainStrength => Mathf.Clamp01(AbsorptionRate * 0.5f);
    // WetSpreadRate → driven by humidity
    private float WetSpreadRate => Mathf.Lerp(0.02f, 0.5f, Humidity);
    
    private PendulumPaintDropper _tester;
    // BrushSizeScale → driven by arm length + amplitude
    private float BrushSizeScale 
    {
        get 
        {

            if (_tester != null)
            {
                float armLength = _tester.ArmLength;
                float amplitude = Mathf.Max(_tester.SwingAmplitudeX, _tester.SwingAmplitudeZ);
                return Mathf.Clamp((armLength * 0.05f) + (amplitude * 0.005f), 0.05f, 2f);
            }
            return 0.35f;
        }
    }


    // ──────────────────────────────────────────────────────────────
    //  الحالة الخاصة (Private State)
    // ──────────────────────────────────────────────────────────────

    private Vector2   _uvGravityDir = new Vector2(0, -1);
    private Texture2D _paintTex;
    private Color[]   _pixels;
    private Color[]   _wetPixels;
    private float[]   _wetTimer;
    private float[]   _thicknessMap;   // ★ سُمك الطلاء المتراكم لكل بكسل
    private float[]   _grainMap;       // ★ نسيج قماش اللوحة (مخبوز مرة واحدة عند الإعداد)
    private bool      _dirty;
    private Renderer  _renderer;
    private int       _minBrushRadius;

    // O(wet) بدلاً من O(1M) لتحديث التجفيف
    private readonly HashSet<int> _wetIndices = new HashSet<int>();


    // عداد تنظيف دوري للقاموس
    private float _cleanupTimer;

    // ──────────────────────────────────────────────────────────────
    //  دورة الحياة (Lifecycle)
    // ──────────────────────────────────────────────────────────────

    void Awake()
    {
        _tester         = FindAnyObjectByType<PendulumPaintDropper>();
        _renderer       = GetComponent<Renderer>();
        _minBrushRadius = Mathf.Max(4, Mathf.RoundToInt(TextureRes * 0.008f));
        InitializeTexture();
    }

    void InitializeTexture()
    {
        int total  = TextureRes * TextureRes;
        _paintTex  = new Texture2D(TextureRes, TextureRes, TextureFormat.RGBA32, false);
        _pixels       = new Color[total];
        _wetPixels    = new Color[total];
        _wetTimer     = new float[total];
        _thicknessMap = new float[total];
        _grainMap     = new float[total];

        BakeGrainMap();   // ★ خبز نسيج القماش مرة واحدة

        for (int i = 0; i < total; i++)
        {
            _pixels[i]    = Color.white;
            _wetPixels[i] = Color.clear;
        }

        _paintTex.SetPixels(_pixels);
        _paintTex.Apply();
        _renderer.material.mainTexture = _paintTex;
    }

    /// <summary>
    /// ★ يخبز خريطة نسيج القماش باستخدام ضجيج بيرلن متعدد الأوكتافات.
    /// الحبوب المرتفعة (grain عالي) = امتصاص أقل. الأخاديد (grain منخفض) = امتصاص أكثر.
    /// يُنفَّذ مرة واحدة فقط لتجنب الحوسبة في كل إطار.
    /// </summary>
    void BakeGrainMap()
    {
        float seed  = Random.Range(0f, 500f);
        int   total = TextureRes * TextureRes;
        for (int i = 0; i < total; i++)
        {
            float px = i % TextureRes;
            float py = i / TextureRes;
            float u  = px * GrainScale / TextureRes + seed;
            float v  = py * GrainScale / TextureRes + seed;
            // ثلاثة أوكتافات: بنية كبيرة + تفاصيل متوسطة + حبيبات صغيرة
            float g  = Mathf.PerlinNoise(u,          v)
                     + 0.50f * Mathf.PerlinNoise(u * 2.1f, v * 2.1f)
                     + 0.25f * Mathf.PerlinNoise(u * 4.3f, v * 4.3f);
            _grainMap[i] = Mathf.Clamp01(g / 1.75f);
        }
    }

    void LateUpdate()
    {
        UpdateDynamicTilt();
        UpdateDrying();
        UpdateWetSpreading();   // ★

        if (_dirty)
        {
            _paintTex.SetPixels(_pixels);
            _paintTex.Apply();
            _dirty = false;
        }
    }

    void UpdateDynamicTilt()
    {
        Vector3 n        = PaintNormalLocal.normalized;
        Vector3 localUp  = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
        Vector3 tangentU = Vector3.Cross(n, localUp).normalized;
        Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

        Vector3 wU = transform.TransformDirection(tangentU);
        Vector3 wV = transform.TransformDirection(tangentV);
        Vector3 wN = transform.TransformDirection(n);

        Vector2 grav = new Vector2(
            Vector3.Dot(Vector3.down, wU),
            Vector3.Dot(Vector3.down, wV)
        );
        _uvGravityDir = grav.sqrMagnitude > 0.001f ? grav.normalized : new Vector2(0, -1);

        if (AutoCalculateTilt)
            TiltAngle = Vector3.Angle(Vector3.up, wN);
    }

    // ──────────────────────────────────────────────────────────────
    //  الواجهة العامة — يتم استدعاؤها من SimulationManager
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// فحص اصطدام + رسم.
    /// ★ الآن: يُقحم ضربة متواصلة بين الإطارات ويتتبع اتجاه الحركة لاستطالة الفرشاة.
    /// يعيد true إذا اصطدمت الجسيمة ورُسم.
    /// </summary>
    public bool TryPaint(FluidParticleData particle, float dt)
    {
        if (!particle.IsActive) return false;

        // ── كشف الاصطدام (كما في الأصل — حاصل ضرب نقطي قوي) ──────
        Vector3 localPos  = transform.InverseTransformPoint(particle.Position);
        Vector3 n         = PaintNormalLocal.normalized;
        float   planeDist = Vector3.Dot(localPos, n);
        if (Mathf.Abs(planeDist) > HitThreshold) return false;

        Vector3 inPlane  = localPos - n * planeDist;
        Vector3 upRef    = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
        Vector3 tangentU = Vector3.Cross(n, upRef).normalized;
        Vector3 tangentV = Vector3.Cross(tangentU, n).normalized;

        float u = Vector3.Dot(inPlane, tangentU);
        float v = Vector3.Dot(inPlane, tangentV);
        if (Mathf.Abs(u) > CanvasWidth * 0.5f || Mathf.Abs(v) > CanvasHeight * 0.5f) return false;

        Vector2 uv = new Vector2(u / CanvasWidth + 0.5f, v / CanvasHeight + 0.5f);

        // ── ★ اتجاه الضربة في فضاء UV من سرعة الجسيمة ───────────
        Vector3 worldVel   = particle.GetVelocity(dt);
        float   speed      = worldVel.magnitude;
        Vector3 velInPlane = worldVel - n * Vector3.Dot(worldVel, n);
        Vector2 strokeDir  = (velInPlane.sqrMagnitude > 0.001f)
            ? new Vector2(Vector3.Dot(velInPlane, tangentU), Vector3.Dot(velInPlane, tangentV)).normalized
            : Vector2.zero;

        // ── حجم الجسيمة ───────────────────────────────────────────
        float refMass = Mathf.Max(ReferenceParticleMass, 0.00001f);
        float massSc  = Mathf.Clamp(Mathf.Sqrt(particle.Mass / refMass),
                                    MassScaleClamp.x, MassScaleClamp.y);

        // ── ★ إقحام بين الإطارات المتتالية ───────────────────────

            Paint(uv, particle.PaintColor, speed, particle.Viscosity, massSc, strokeDir);

        particle.State      = FluidParticleData.ParticleState.Splattered;
        return true;
    }

    /// <summary>يمسح اللوحة ويعيدها للون الأبيض.</summary>
    public void Clear()
    {
        int total = TextureRes * TextureRes;
        for (int i = 0; i < total; i++)
        {
            _pixels[i]       = Color.white;
            _wetPixels[i]    = Color.clear;
            _wetTimer[i]     = 0f;
            _thicknessMap[i] = 0f;
        }
        _wetIndices.Clear();
        _dirty = true;
    }

    /// <summary>يحفظ اللوحة الحالية كصورة PNG في جذر المشروع.</summary>
    public void SaveTextureToPNG(string fileName = "PaintCanvas")
    {
        byte[] png  = _paintTex.EncodeToPNG();
        string path = System.IO.Path.Combine(Application.dataPath, "..", fileName + ".png");
        System.IO.File.WriteAllBytes(path, png);
        Debug.Log($"[PaintCanvas] تم الحفظ في: {path}");
    }

    // ──────────────────────────────────────────────────────────────
    //  الرسم الأساسي
    // ──────────────────────────────────────────────────────────────

    /// <summary>ضربة فرشاة منفردة عند نقطة UV (بدون إقحام).</summary>
    void Paint(Vector2 uv, Color color, float speed, float viscosity, float massScale, Vector2 strokeDir)
    {
        int   radius   = ComputeRadius(uv, speed, viscosity, massScale);
        Color tinted   = SpeedTint(color, speed);
        float strength = Mathf.Clamp(massScale, 0.25f, 3f);
        float aspect   = ComputeAspect(speed);

        DrawSpot(uv, radius, tinted, true, strength, strokeDir, aspect);

        if (speed > 1.5f || viscosity < 2.0f)
            DrawSplatter(uv, radius, tinted, speed, strength, viscosity);

        if (TiltAngle > 5f && viscosity < 5.0f)
            StartCoroutine(DrawDrip(uv, radius, tinted, strength, viscosity));
    }


    // ──────────────────────────────────────────────────────────────
    //  مساعدات الحساب
    // ──────────────────────────────────────────────────────────────

    /// <summary>★ يحسب نصف قطر الفرشاة بالبكسل مع تأثير نسيج القماش المحلي.</summary>
    int ComputeRadius(Vector2 uv, float speed, float viscosity, float massScale)
    {
        float speedF = Mathf.Clamp01(speed / 6f);
        float absF   = 1f - AbsorptionRate * 0.4f;
        float viscF  = Mathf.Clamp(1.5f / Mathf.Max(viscosity, 0.1f), 0.5f, 2.0f);
        float r      = 0.03f * (0.5f + speedF) * absF * viscF * Mathf.Clamp(massScale, 0.5f, 3f);

        // ★ نسيج القماش: الحبوب الخشنة تُوسّع الانتشار قليلاً
        int   cx = Mathf.Clamp(Mathf.RoundToInt(uv.x * TextureRes), 0, TextureRes - 1);
        int   cy = Mathf.Clamp(Mathf.RoundToInt(uv.y * TextureRes), 0, TextureRes - 1);
        float g  = _grainMap[cy * TextureRes + cx];
        r       *= 1f + GrainStrength * (g - 0.5f) * 0.4f;

return Mathf.Max(2, Mathf.RoundToInt(r * TextureRes * BrushSizeScale));
    }

    /// <summary>★ نسبة الاستطالة: البندول أسرع → ضربة أطول وأضيق.</summary>
    float ComputeAspect(float speed)
    {
        float dynamicMaxStrokeAspect = Mathf.Clamp(1f + speed * 0.5f, 1f, 4f);
        return Mathf.Lerp(1f, dynamicMaxStrokeAspect, Mathf.Clamp01(speed / 6f) * 0.7f);
    }

    /// <summary>الاصطدام السريع → لون أفتح قليلاً (الطلاء يُرشّ أرق).</summary>
    static Color SpeedTint(Color color, float speed)
    {
        float t = Mathf.Clamp01(speed / 6f) * 0.4f;
        return Color.Lerp(color, Color.Lerp(color, Color.white, 0.25f), t);
    }

    // ──────────────────────────────────────────────────────────────
    //  رسم البقعة  ★ بيضاوية موجّهة + نسيج القماش
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// يرسم بقعة بيضاوية عضوية.
    ///
    /// ★ strokeDir (مُطبَّع) يحدد محور الاستطالة.
    ///   aspect > 1 → شبه القطر على الامتداد = radius × aspect.
    ///   aspect = 1 → دائري (عند نقاط الاستدارة أو الرذاذ).
    ///
    /// ★ نسيج القماش يخفف الشفافية محلياً (الأخاديد تمتص أكثر).
    /// </summary>
    void DrawSpot(Vector2 uv, int radius, Color color, bool addNoise, float strength,
                  Vector2 strokeDir = default, float aspect = 1f)
    {
        float noiseOX = Random.Range(0f, 1000f);
        float noiseOY = Random.Range(0f, 1000f);

        int cx    = Mathf.RoundToInt(uv.x * TextureRes);
        int cy    = Mathf.RoundToInt(uv.y * TextureRes);
        // صندوق الفحص يغطي البيضاوية بأكملها في أي اتجاه
        int scanR = Mathf.RoundToInt(radius * (Mathf.Max(aspect, 1f) + 1f) * 1.1f);

        // ★ مصفوفة دوران من اتجاه الضربة
        //   strokeDir = (cosθ, sinθ) → تحويل إلى الإطار المحلي للضربة
        bool  hasDir = strokeDir.sqrMagnitude > 0.001f;
        float cosA   = hasDir ? strokeDir.x : 1f;
        float sinA   = hasDir ? strokeDir.y : 0f;

        for (int px = cx - scanR; px <= cx + scanR; px++)
        for (int py = cy - scanR; py <= cy + scanR; py++)
        {
            if ((uint)px >= (uint)TextureRes || (uint)py >= (uint)TextureRes) continue;

            float dx = px - cx;
            float dy = py - cy;

            // ★ تحويل إلى الإطار المحلي للضربة
            float lx =  dx * cosA + dy * sinA;   // المكوّن على امتداد الضربة
            float ly = -dx * sinA + dy * cosA;   // المكوّن العرضي (⊥ للضربة)

            // ★ مسافة بيضاوية: شبه قطر الامتداد = radius×aspect، العرضي = radius
            //   (lx/aspect)² + ly² ≤ radius²  →  normDist ≤ radius
            float normDist = Mathf.Sqrt((lx / aspect) * (lx / aspect) + ly * ly);

            float noiseVal    = addNoise
                ? Mathf.PerlinNoise(px * 0.12f + noiseOX, py * 0.12f + noiseOY) * radius * 0.45f
                : 0f;
            float effRadius   = radius + noiseVal;
            if (normDist > effRadius) continue;

            // ★ نسيج القماش: يخفف الشفافية في الأخاديد (grain منخفض)
            float grain       = _grainMap[py * TextureRes + px];
            float grainFactor = Mathf.Lerp(1f, 0.3f, GrainStrength * (1f - grain));

            // Smoothstep: مركز معتم، حافة ناعمة
            float t      = normDist / (effRadius + 0.5f);
            float smooth = t * t * (3f - 2f * t);
            float alpha  = Mathf.Pow(1f - smooth, 0.4f)
                         * Mathf.Lerp(0.7f, 1.35f, Mathf.Clamp01(strength - 1f))
                         * grainFactor;

            BlendPixel(px, py, color, Mathf.Clamp01(alpha));
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  الرذاذ (Splatter)
    // ──────────────────────────────────────────────────────────────

    void DrawSplatter(Vector2 centerUV, int mainRadius, Color color, float speed, float strength, float viscosity)
    {
        float viscMult = Mathf.Clamp(1.5f / Mathf.Max(viscosity, 0.1f), 0.5f, 2.5f);
        
        // SplatterCount driven by impact speed
        float dynamicSplatterCount = Mathf.Clamp(speed * 2f, 0f, 20f);
        int   droplets = Mathf.RoundToInt(dynamicSplatterCount * Mathf.Clamp01(speed / 8f) * viscMult);

        // SplatterSpread driven by speed + viscosity
        float dynamicSplatterSpread = Mathf.Clamp01((speed * 0.05f) + (1f / Mathf.Max(viscosity, 0.1f)) * 0.1f);

        for (int i = 0; i < droplets; i++)
        {
            float   angle    = Random.Range(0f, Mathf.PI * 2f);
            float   dist     = Random.Range(1.5f, 3.2f) * mainRadius / (float)TextureRes
                               * dynamicSplatterSpread * viscMult;
            Vector2 dropUV   = centerUV + new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);

            if (dropUV.x < 0f || dropUV.x > 1f || dropUV.y < 0f || dropUV.y > 1f) continue;

            int dropR = Mathf.Max(Mathf.RoundToInt(_minBrushRadius * 0.3f),
                                  Mathf.RoundToInt(mainRadius * Random.Range(0.06f, 0.22f)));
            DrawSpot(dropUV, dropR, color, false, strength * 0.75f);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  التقطير  ★ يستجيب لسُمك الطلاء المتراكم
    // ──────────────────────────────────────────────────────────────

    IEnumerator DrawDrip(Vector2 startUV, int width, Color color, float strength, float viscosity)
    {
        float viscMult     = Mathf.Clamp(1.5f / Mathf.Max(viscosity, 0.1f), 0.3f, 3.0f);
        float tiltFactor   = Mathf.Clamp01(TiltAngle / 90f);
        float absorpFactor = 1f - AbsorptionRate;

        // ★ السُمك المتراكم عند نقطة البداية يُطيل التقطير
        int   cx         = Mathf.Clamp(Mathf.RoundToInt(startUV.x * TextureRes), 0, TextureRes - 1);
        int   cy         = Mathf.Clamp(Mathf.RoundToInt(startUV.y * TextureRes), 0, TextureRes - 1);
        float localThick = _thicknessMap[cy * TextureRes + cx];
        float thickBoost = Mathf.Clamp(localThick * 2f, 0f, 1.5f);

        float dripLength = Random.Range(0.04f, 0.2f) * viscMult * tiltFactor * absorpFactor * (1f + thickBoost);
        int   steps      = Mathf.RoundToInt(dripLength * TextureRes);
        Vector2 current  = startUV;

        Vector2 latDir  = new Vector2(-_uvGravityDir.y, _uvGravityDir.x);
        Vector2 dripDir = (_uvGravityDir + latDir * Random.Range(-0.15f, 0.15f)).normalized * (1f / TextureRes);

        for (int s = 0; s < steps; s++)
        {
            float t   = (float)s / steps;
            int   r   = Mathf.Max(1, Mathf.RoundToInt(width * Mathf.Lerp(0.55f, 0.08f, t)));
            // ★ نمرر alpha عبر قناة اللون — BlendPixel يضربها في حساباته
            Color faded = new Color(color.r, color.g, color.b, Mathf.Lerp(0.75f, 0f, t));
            DrawSpot(current, r, faded, false, strength * 0.6f);
            current += dripDir;

            if (current.y < 0f || current.y > 1f) break;
            yield return new WaitForSeconds(0.012f);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  دمج البكسلات  ★ CMY + نسيج القماش + خريطة السُمك
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// يدمج لوناً جديداً في البكسل.
    ///
    /// ★ المزج الطرحي (CMY): الطلاء الجديد يخلط مع الطبقة الرطبة كأصباغ حقيقية.
    /// ★ نسيج القماش: يُضعف الشفافية في الأخاديد ويُعزّزها على الحبوب.
    /// ★ خريطة السُمك: تتراكم مع كل طبقة وتؤثر على الجفاف والتقطير.
    /// ★ color.a: يُستخدم كمضاعف إضافي (يسمح للتقطير بالتلاشي عبر القناة الشفافة).
    /// </summary>
    void BlendPixel(int x, int y, Color newColor, float alpha)
    {
        int idx = y * TextureRes + x;
        if ((uint)idx >= (uint)_pixels.Length) return;

        Color existing = _pixels[idx];
        bool  isWet    = _wetTimer[idx] > 0f;

        // ★ نسيج القماش يتحكم في الامتصاص محلياً
        float grain      = _grainMap[idx];
        float grainAbsorp = Mathf.Lerp(1f, 0.35f, GrainStrength * (1f - grain));
        // ★ color.a كمضاعف (للتقطير المتلاشي وأي ألوان شفافة)
        float finalAlpha = Mathf.Clamp01(alpha * newColor.a * grainAbsorp);

        // لون الأصباغ بدون الشفافية (للمزج)
        Color solidColor = new Color(newColor.r, newColor.g, newColor.b, 1f);

        Color paintColor;
        if (isWet && Humidity > 0.2f)
        {
            // ★ مزج طرحي: الطلاء الجديد يمتزج مع الطبقة الرطبة كأصباغ حقيقية
            paintColor = SubtractiveMix(solidColor, _wetPixels[idx], Humidity * 0.7f);
        }
        else
        {
            paintColor = solidColor;
        }

        // تركيب اللون المختار فوق اللوحة
        Color finalColor = Color.Lerp(existing, paintColor, finalAlpha);

        _pixels[idx]    = finalColor;
        _wetPixels[idx] = finalColor;

        // ★ تراكم السُمك — يُغذّي التقطير والانتشار والجفاف البطيء
        _thicknessMap[idx] = Mathf.Clamp(_thicknessMap[idx] + finalAlpha * 0.5f, 0f, 3f);

        // وقت الجفاف يتناسب مع الرطوبة والسُمك (الطلاء السميك يجف أبطأ)
        float dryTime  = Mathf.Lerp(0.4f, 6f, Humidity) * (1f + _thicknessMap[idx] * 0.3f);
        _wetTimer[idx] = dryTime;
        _wetIndices.Add(idx);

        _dirty = true;
    }

    /// <summary>
    /// ★ مزج الأصباغ الطرحي (فضاء CMY).
    ///
    /// يحاكي الخلط المادي الحقيقي للألوان:
    ///   أحمر (CMY = 0,1,1) + أصفر (CMY = 0,0,1) → برتقالي ✓
    ///   أحمر + أزرق → بنفسجي ✓
    ///   الألوان تتحول للغمق عند الدمج (تدهور طفيف = تأثير التشبع).
    ///
    /// مقارنةً بـ Color.Lerp العادي (RGB — تضافي):
    ///   RGB: أحمر + أزرق → رمادي وردي ✗
    ///   CMY: أحمر + أزرق → بنفسجي حقيقي ✓
    /// </summary>
    static Color SubtractiveMix(Color c1, Color c2, float t)
    {
        // RGB → CMY
        Vector3 cmy1 = new Vector3(1f - c1.r, 1f - c1.g, 1f - c1.b);
        Vector3 cmy2 = new Vector3(1f - c2.r, 1f - c2.g, 1f - c2.b);

        // مزج في فضاء CMY
        Vector3 mixed = Vector3.Lerp(cmy1, cmy2, t);

        // تدهور طفيف عند الدمج (الأصباغ تمتص أكثر عند تداخلها)
      //  mixed += Vector3.one * (t * (1f - t) * 0.06f);

        // CMY → RGB
        return new Color(
            Mathf.Clamp01(1f - mixed.x),
            Mathf.Clamp01(1f - mixed.y),
            Mathf.Clamp01(1f - mixed.z),
            Mathf.Lerp(c1.a, c2.a, t)
        );
    }

    // ──────────────────────────────────────────────────────────────
    //  انتشار الطلاء الرطب  ★ جديد
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ ينزّف الطلاء الرطب ببطء إلى البكسلات المجاورة (تأثير شعري).
    ///
    /// الشروط: البكسل رطب + سُمك كافٍ + رطوبة ≥ 0.3
    /// يُعالج جزءاً محدوداً في كل إطار (maxProcess) للأداء.
    /// </summary>
    void UpdateWetSpreading()
    {
        if (_wetIndices.Count == 0 || Humidity < 0.3f) return;

        int maxProcess  = Mathf.Min(_wetIndices.Count, 120);
        int processed   = 0;
        var spreadQueue = new List<(int x, int y, Color c, float a)>(64);

        foreach (int idx in _wetIndices)
        {
            if (processed++ >= maxProcess) break;
            if (_thicknessMap[idx] < 0.05f) continue;

            float prob = _thicknessMap[idx] * WetSpreadRate * Humidity;
            if (Random.value > prob) continue;

            int   x       = idx % TextureRes;
            int   y       = idx / TextureRes;
            float spreadA = 0.025f * _thicknessMap[idx] * Humidity;

            // ★ الانتشار في الاتجاهات الأربعة
            QueueSpread(spreadQueue, x + 1, y,     _wetPixels[idx], spreadA);
            QueueSpread(spreadQueue, x - 1, y,     _wetPixels[idx], spreadA);
            QueueSpread(spreadQueue, x,     y + 1, _wetPixels[idx], spreadA);
            QueueSpread(spreadQueue, x,     y - 1, _wetPixels[idx], spreadA);

            // ★ انتشار إضافي في اتجاه الجاذبية (الطلاء الرطب يسيل للأسفل)
            int gx = Mathf.RoundToInt(_uvGravityDir.x);
            int gy = Mathf.RoundToInt(_uvGravityDir.y);
            if (gx != 0 || gy != 0)
                QueueSpread(spreadQueue, x + gx, y + gy, _wetPixels[idx], spreadA * 1.5f);
        }

        // التطبيق بعد اكتمال الفحص — يتجنب التعديل أثناء التكرار
        foreach (var (sx, sy, col, alp) in spreadQueue)
            BlendPixel(sx, sy, col, alp);
    }

    void QueueSpread(List<(int, int, Color, float)> q, int x, int y, Color c, float a)
    {
        if ((uint)x < (uint)TextureRes && (uint)y < (uint)TextureRes)
            q.Add((x, y, c, a));
    }

    // ──────────────────────────────────────────────────────────────
    //  التجفيف  ★ مع تراجع تدريجي للسُمك
    // ──────────────────────────────────────────────────────────────

    void UpdateDrying()
    {
        if (_wetIndices.Count == 0) return;

        float    dt       = Time.deltaTime;
        var      toRemove = new List<int>(32);

        foreach (int idx in _wetIndices)
        {
            _wetTimer[idx] -= dt;

            // ★ السُمك ينكمش تدريجياً أثناء الجفاف (الطلاء يتقلّص عند الجفاف)
            if (_thicknessMap[idx] > 0f)
                _thicknessMap[idx] = Mathf.Max(0f, _thicknessMap[idx] - dt * 0.015f);

            if (_wetTimer[idx] <= 0f)
            {
                _wetTimer[idx]  = 0f;
                _wetPixels[idx] = Color.clear;
                toRemove.Add(idx);
            }
        }

        foreach (int idx in toRemove)
            _wetIndices.Remove(idx);
    }
    }
