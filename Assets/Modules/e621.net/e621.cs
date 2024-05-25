using Newtonsoft.Json;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class e621 : MonoBehaviour
{
    public class ModSettingsJSON
    {
        public bool allowExplicit;
        public uint fetchAttempts;
    }

    public KMAudio Audio;
    public KMBombModule Module;
    public KMBombInfo Info;
    public KMModSettings ModSettings;
    public KMSelectable Button, SLButton;
    public Mesh ComponentMesh, CubeMesh;
    public Renderer Component;
    public TextMesh Text;
    public Texture ComponentTexture;
    public Transform Highlight;

    bool isSolved = false;

    private static bool _lightsOn = false;
    private bool _inputMode = false, _ready = false, _tpInputting = false;
    private byte _pressed;
    private static int _moduleIdCounter = 1;
    private int _moduleId = 0;
    private string _solution = "";
    private float _extraDelay = 1f, _inputEndTime = 0f;

    private void FixedUpdate()
    {
        //lights off or is solved should end it here
        if (!_lightsOn || isSolved)
            return;

        if (_inputMode && Time.time > _inputEndTime)
        {
            if (_tpInputting)
                return;

            StartCoroutine(Blink());
            _inputMode = false;
            _pressed = 0;

            if (Text.text[Text.text.Length - 1] != _solution[Text.text.Length - 1])
            {
                Debug.LogFormat("[e621.net #{0}] Strike! The user submitted {1} instead of {2} on number {3}!", _moduleId, Text.text[Text.text.Length - 1], _solution[Text.text.Length - 1], Text.text.Length);
                Module.HandleStrike();
                return;
            }

            if (Text.text == _solution)
            {
                isSolved = true;

                ResetVisuals();

                Audio.PlaySoundAtTransform("soundE621", Module.transform);
                Debug.LogFormat("[e621.net #{0}] The correct number was submitted, solved!", _moduleId);
                Module.HandlePass();
                return;
            }

            Text.text += _pressed.ToString();
            _pressed = 0;
        }
    }

    private void ResetVisuals()
    {
        Text.text = "";
        Component.material.mainTexture = ComponentTexture;
        Component.GetComponent<MeshFilter>().mesh = ComponentMesh;
        Component.transform.localScale = new Vector3(1, 1, 1);
        Highlight.transform.localScale = new Vector3(0.2f, 0.015f, 0.2f);
        Text.color = new Color32(0xFE, 0xB2, 0x29, 0xFF);
        Text.fontSize = 300;
    }

    /// <summary>
    /// Code that runs when bomb is loading.
    /// </summary>
    private void Start()
    {
        Module.OnActivate += Init;
        _moduleId = _moduleIdCounter++;
    }

    /// <summary>
    /// Initalising buttons.
    /// </summary>
    private void Awake()
    {
        Button.OnInteract += delegate ()
        {
            HandlePress();
            return false;
        };
        SLButton.OnInteract += delegate ()
        {
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Module.transform);
            SLButton.AddInteractionPunch(10f);

            if (!_ready)
                return false;

            Debug.LogFormat("[e621.net #{0}] Fetching a new image after {1} seconds...", _moduleId, _extraDelay - 1f);
            StartCoroutine(Load());
            return false;
        };
    }

    /// <summary>
    /// Creates new arrows and logs answer.
    /// </summary>
    private void Init()
    {
        StartCoroutine(Load());
        _lightsOn = true;
    }

    /// <summary>
    /// If first press, start sequence. Otherwise, log each press in a temporary list.
    /// </summary>
    private void HandlePress()
    {
        //sounds and punch effect
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Module.transform);
        //Audio.PlaySoundAtTransform("start", Module.transform);

        //lights off or is solved should end it here
        if (!_lightsOn || isSolved || !_ready)
            return;

        _inputMode = true;
        _pressed = (byte)(++_pressed % 10);
        _inputEndTime = Time.time + 0.5f;

        Text.text = Text.text.Substring(0, Text.text.Length - 1) + _pressed;
    }

    private IEnumerator Load()
    {
        _ready = false;
        ResetVisuals();

        if (_extraDelay > 1f)
        {
            float end = Time.time + _extraDelay;
            while (Time.time < end)
            {
                Text.text = string.Format("Waiting\n{0:F2}\nseconds...", end - Time.time);
                yield return null;
            }
        }
        _extraDelay *= 3f;

        bool allowExplicit = false;
        uint fetchAttempts = 100;
        try
        {
            ModSettingsJSON settings = JsonConvert.DeserializeObject<ModSettingsJSON>(ModSettings.Settings);
            if (settings != null)
            {
                Debug.LogFormat("[e621.net #{0}] JSON read successfully.", _moduleId);
                allowExplicit = settings.allowExplicit;
                fetchAttempts = settings.fetchAttempts;
            }
            else
                Debug.LogFormat("[e621.net #{0}] JSON accessed, but is empty.", _moduleId);
        }
        catch (JsonReaderException e)
        {
            Debug.LogFormat("[e621.net #{0}] JSON reading failed with error {1}, extracting default values.", _moduleId, e.Message);
        }

        int rnd = 0;
        for (uint i = 0; i < fetchAttempts; i++)
        {
            Text.text = "Fetching... (" + (i + 1) + "/" + fetchAttempts + " tries)";
            Text.fontSize = 150;

            string baseuri = allowExplicit ? "https://e621.net/" : "https://e926.net/";
            string uri = baseuri + "posts.json?limit=1&tags=order:random+~type:png+~type:jpg";

            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                webRequest.SetRequestHeader("User-Agent", "KTANEModule/e621/V1.1 (by Emik)");

                // Request and wait for the desired page.
                yield return webRequest.SendWebRequest();

                Match match;
                Regex regex1, regex2, regex3, regex4;
                if (!allowExplicit)
                {
                    regex1 = new Regex(@"""rating"":""s""");

                    match = regex1.Match(webRequest.downloadHandler.text);

                    if (!match.Success)
                        continue;
                }

                regex2 = new Regex(@"https:\/\/static1\.e(621|926)\.net\/data\/[0-9a-f]{2}\/[0-9a-f]{2}\/[0-9a-f]{32}\.(pn|jp)g");
                regex3 = new Regex(@"https:\/\/static1\.e(621|926)\.net\/data\/sample\/[0-9a-f]{2}\/[0-9a-f]{2}\/[0-9a-f]{32}\.(pn|jp)g");

                regex4 = new Regex(@"""id"":(\d+)");

                match = regex4.Match(webRequest.downloadHandler.text);
                if (!match.Success)
                    continue;

                rnd = int.Parse(match.Groups[1].Value);

                match = regex2.Match(webRequest.downloadHandler.text);

                if (!match.Success)
                    match = regex3.Match(webRequest.downloadHandler.text);

                if (match.Success)
                {
                    WWW wwwLoader = new WWW(match.Value);
                    yield return wwwLoader;

                    float width = wwwLoader.texture.width, height = wwwLoader.texture.height;
                    var cls = new Vector3(width / Mathf.Max(width, height) / 5, 0.03f, height / Mathf.Max(width, height) / 5);

                    if (Component.transform.localScale.x < 0.1f || Component.transform.localScale.z < 0.1f)
                        continue;

                    Component.transform.localScale = cls;
                    Highlight.transform.localScale = new Vector3((width / Mathf.Max(width, height) / 5) + 0.01f, 0.015f, (height / Mathf.Max(width, height) / 5) + 0.01f);

                    Component.material.mainTexture = wwwLoader.texture;
                    Component.GetComponent<MeshFilter>().mesh = CubeMesh;

                    Audio.PlaySoundAtTransform("soundE621", Module.transform);
                    Button.AddInteractionPunch();
                    Debug.LogFormat("[e621.net #{0}] The module has successfully fetched {1}", _moduleId, match.Value);
                    _ready = true;
                    break;
                }

                yield return new WaitForSeconds(1f);
            }
        }

        Text.transform.localPosition = new Vector3(0, 0.0151f, 0);
        Text.text = "0";
        Text.fontSize = 300;
        StartCoroutine(Blink());

        if (!_ready)
        {
            rnd = 0;
            Debug.LogFormat("[e621.net #{0}] Failed to establish connection with e621.net/e926.net, resorting to default answer...", _moduleId);
            _ready = true;
        }

        _solution = rnd.ToString();
        Debug.LogFormat("[e621.net #{0}] The expected solution is {1}.", _moduleId, _solution);
    }

    private IEnumerator Blink()
    {
        if (_solution == "0")
            yield break;

        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.WireSequenceMechanism, Module.transform);
        Button.GetComponent<Renderer>().material.color = new Color32(0, 0, 0, 255);
        Text.color = new Color32(0, 0, 0, 255);

        yield return new WaitForSeconds(0.1f);

        Texture2D tex2D = (Texture2D)Button.GetComponent<Renderer>().material.mainTexture;
        Color32 textColor = AverageColorFromTexture(tex2D);

        Button.GetComponent<Renderer>().material.color = new Color32(255, 255, 255, 255);
        Text.color = new Color32((byte)(255 - textColor.r), (byte)(255 - textColor.g), (byte)(255 - textColor.b), 255);
    }

    Color32 AverageColorFromTexture(Texture2D tex)
    {
        Color32[] texColors = tex.GetPixels32();
        int total = texColors.Length;
        float r = 0, g = 0, b = 0;

        for (int i = 0; i < total; i++)
        {
            r += texColors[i].r;
            g += texColors[i].g;
            b += texColors[i].b;
        }

        return new Color32((byte)(r / total), (byte)(g / total), (byte)(b / total), 255);
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} submit <#> (Submits those digits)";
#pragma warning restore 414

    /// <summary>
    /// TwitchPlays Compatibility, detects every chat message and clicks buttons accordingly.
    /// </summary>
    /// <param name="command">The twitch command made by the user.</param>
    IEnumerator ProcessTwitchCommand(string command)
    {
        Regex rx = new Regex(@"^\s*(?:submit\s*)?(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Match m = rx.Match(command);

        //if command is formatted correctly
        if (m.Success)
        {
            yield return null;
            foreach (char c in m.Groups[1].Value)
            {
                int v = int.Parse(c.ToString());
                if (v == 0)
                    v = 10;
                _tpInputting = true;
                while (_pressed != v)
                {
                    Button.OnInteract();
                    yield return new WaitForSeconds(0.1f);
                }
                _tpInputting = false;
                while (_inputMode)
                    yield return true;
            }
        }
    }

    /// <summary>
    /// Force the module to be solved in TwitchPlays
    /// </summary>
    IEnumerator TwitchHandleForcedSolve()
    {
        var it = ProcessTwitchCommand("submit " + _solution.Substring(Text.text.Length - 1));
        while (it.MoveNext())
            yield return it.Current;
    }
}
