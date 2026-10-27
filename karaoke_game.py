"""Karaoke Desafío Musical - juego de fiesta con PyGame, voz "Alexa" (edge-tts) y 2 equipos."""
import asyncio
import json
import math
import os
import queue
import random
import re
import shutil
import sys
import tempfile
import threading
from pathlib import Path

import pygame

try:
    import edge_tts
except ImportError:
    edge_tts = None
try:
    import pyttsx3  # respaldo sin internet (voz de Windows)
except ImportError:
    pyttsx3 = None

APP_NAME = "KaraokeDesafio"
W, H = 1200, 777                      # misma proporción que el skin (2576 x 1667)
K = W / 2576                          # los datos del skin están en píxeles originales
BLANCO, GRIS, AMARILLO, VERDE = (245, 245, 245), (170, 175, 190), (255, 214, 40), (70, 230, 120)
AUDIO_EXT = {".mp3", ".wav", ".ogg"}


def sx(v):
    return int(round(v * K))


def RC(x0, y0, x1, y1):
    return pygame.Rect(sx(x0), sx(y0), sx(x1 - x0), sx(y1 - y0))


def PT(x, y):
    return (sx(x), sx(y))


# ---- posiciones de cada elemento en el skin (píxeles originales) ----
STAGE, STAGE_RADIUS = (64, 765, 2510, 1619), 60
TEXT_AREA = (120, 800, 2060, 1560)
NUM_C, SUB_C, WAVE_C = (950, 190), (1390, 258), (1895, 202)
SCORE_C = {"rojo": (450, 582), "azul": (2395, 582)}
OVAL_C, BAR = (760, 610), (915, 563, 1960, 634)
LAMPS = {"izq": ([733, 845, 960], 450), "der": ([1687, 1800, 1911], 450)}
LABEL = (1040, 370, 1600, 530)
TEAM_R = {"rojo": (30, 360, 620, 712), "azul": (2000, 360, 2550, 712)}
LOGO_BOX = {"logo_izq": (40, 20, 470, 330), "logo_der": (2180, 20, 2520, 330)}

VOCES = [
    ("es-MX-DaliaNeural", "Dalia (México)"), ("es-MX-JorgeNeural", "Jorge (México)"),
    ("es-ES-ElviraNeural", "Elvira (España)"), ("es-US-PalomaNeural", "Paloma (EE. UU.)"),
    ("es-AR-ElenaNeural", "Elena (Argentina)"), ("es-CO-SalomeNeural", "Salomé (Colombia)"),
]
TECLAS = {  # acción: (etiqueta, tecla por defecto)
    "hablar": ("Alexa dice el texto de la canción", "space"),
    "musica": ("Pista: iniciar / detener", "p"),
    "siguiente": ("Siguiente canción", "pagedown"),
    "anterior": ("Canción anterior", "pageup"),
    "calificar": ("Calificador al azar", "c"),
    "rojo": ("Sumar calificación al equipo ROJO", "left"),
    "azul": ("Sumar calificación al equipo AZUL", "right"),
    "deshacer": ("Deshacer la última suma", "backspace"),
}
RANGOS = [(0, 3, "0 a 3 puntos"), (4, 5, "4 a 5 puntos"), (6, 7, "6 a 7 puntos"), (8, 9, "8 a 9 puntos"),
          (10, 10, "10 puntos")]
IMAGENES = {"fondo": "Fondo del escenario", "logo_izq": "Logotipo izquierdo", "logo_der": "Logotipo derecho"}

DEFAULT_CONFIG = {
    "voz": "es-MX-DaliaNeural", "velocidad_voz": 0, "volumen_musica": 80, "volumen_voz": 100,
    "anunciar": True, "fondo_animado": True,
    "teclas": {k: v[1] for k, v in TECLAS.items()},
    "imagenes": {"fondo": "", "logo_izq": "", "logo_der": ""},
    "textos": {
        "bienvenida": "¡Hola a todos! Soy Alexa y estoy lista para el desafío musical. ¡Que empiece la fiesta!",
        "calificacion": "Tu calificación es {puntos}. {frase}",
    },
    "frases": ["¡Puedes mejorar!", "¡Nada mal, sigue intentándolo!", "¡Muy bien, buen trabajo!",
               "¡Excelente actuación!", "¡Perfecto, eres una estrella!"],
    "canciones": [
        {"nombre": "Desafío 1", "texto": "Desafío uno: canten una canción que mencione un color.", "pista": "", "voz": ""},
        {"nombre": "Desafío 2", "texto": "Desafío dos: canten una canción con un nombre de mujer.", "pista": "", "voz": ""},
    ],
}
TECLAS_FIJAS = {"escape", "m", "b", "r", "h", "f9", "f11", "return", "up", "down"} | set("0123456789")
KEY_LABELS = {"left": "←", "right": "→", "up": "↑", "down": "↓", "return": "ENTER", "space": "ESPACIO",
              "pageup": "RE PÁG", "pagedown": "AV PÁG", "backspace": "RETROCESO"}


def key_label(name):
    return KEY_LABELS.get(name, name.upper())


# ----------------------------------------------------------------- utilidades
def resource_path(rel):
    return Path(getattr(sys, "_MEIPASS", Path(__file__).parent)) / rel


def config_path():
    appdata = os.getenv("APPDATA")
    d = (Path(appdata) / APP_NAME) if appdata else (Path.home() / f".{APP_NAME}")
    try:
        d.mkdir(parents=True, exist_ok=True)
    except OSError:
        d = Path(tempfile.gettempdir())
    return d / "config.json"


def user_dir():
    """Carpeta junto al .exe donde se guardan pistas/, voz/ e imagenes/."""
    base = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent
    for b in (base, config_path().parent):
        try:
            for n in ("pistas", "voz", "imagenes"):
                (b / n).mkdir(parents=True, exist_ok=True)
            return b
        except OSError:
            continue
    return base


def load_config():
    cfg = json.loads(json.dumps(DEFAULT_CONFIG))
    try:
        data = json.loads(config_path().read_text(encoding="utf-8"))
        for k, v in data.items():
            if k == "textos" and isinstance(v, dict):
                cfg["textos"].update(v)
            elif k in ("teclas", "imagenes") and isinstance(v, dict):
                cfg[k].update({a: b for a, b in v.items() if a in cfg[k]})
            elif k == "frases" and isinstance(v, list) and len(v) == len(RANGOS):
                cfg[k] = v
            elif k == "canciones" and isinstance(v, list) and v:
                cfg[k] = [{"nombre": str(s.get("nombre", "")), "texto": str(s.get("texto", "")),
                           "pista": str(s.get("pista", "")), "voz": str(s.get("voz", ""))} for s in v]
            elif k in cfg and k not in ("textos", "teclas", "imagenes", "frases", "canciones"):
                cfg[k] = v
    except (OSError, ValueError, AttributeError):
        pass
    return cfg


def save_config(cfg):
    try:
        config_path().write_text(json.dumps(cfg, ensure_ascii=False, indent=2), encoding="utf-8")
    except OSError:
        pass


def natural_key(s):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", s)]


def split_name(stem):
    """'17 AÑOS ALEXA' -> ('17 AÑOS', 'voz');  'La Bomba MUSICA' / '... AUDIO' -> (..., 'pista')."""
    s = " ".join(stem.split())
    low = s.lower()
    for suf in ("alexa",):
        if low.endswith(" " + suf):
            return s[:-len(suf)].strip(), "voz"
    for suf in ("musica", "música", "audio", "pista"):
        if low.endswith(" " + suf):
            return s[:-len(suf)].strip(), "pista"
    return s, "pista"


def tk_dialog(kind, **kw):
    try:
        import tkinter as tk
        from tkinter import filedialog
        root = tk.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        r = {"file": lambda: filedialog.askopenfilename(**kw), "dir": lambda: filedialog.askdirectory(**kw),
             "clip": root.clipboard_get}[kind]()
        root.destroy()
        return r
    except Exception:
        return ""


def wrap(text, font, max_w):
    lines, line = [], ""
    for word in text.split():
        test = f"{line} {word}".strip()
        if font.size(test)[0] <= max_w:
            line = test
        else:
            if line:
                lines.append(line)
            line = word
    if line:
        lines.append(line)
    return lines


def cover(img, size):
    w, h = img.get_size()
    s = max(size[0] / w, size[1] / h)
    nw, nh = int(w * s) + 1, int(h * s) + 1
    img = pygame.transform.smoothscale(img, (nw, nh))
    return img.subsurface(((nw - size[0]) // 2, (nh - size[1]) // 2, size[0], size[1])).copy()


def fit(img, box):
    w, h = img.get_size()
    s = min(box[0] / w, box[1] / h)
    return pygame.transform.smoothscale(img, (max(1, int(w * s)), max(1, int(h * s))))


# ------------------------------------------------------------------ voz Alexa
class Alexa:
    """Genera audio en un hilo aparte para no congelar la interfaz."""

    def __init__(self):
        self.q = queue.Queue()
        self.busy = False
        self.n = 0

    def speak(self, text, voz, velocidad):
        if self.busy or not text.strip():
            return False
        self.busy = True
        self.n += 1
        base = Path(tempfile.gettempdir()) / f"karaoke_tts_{os.getpid()}_{self.n}"
        threading.Thread(target=self._work, args=(text, voz, velocidad, base), daemon=True).start()
        return True

    def _work(self, text, voz, velocidad, base):
        error = "edge-tts no está instalado"
        if edge_tts:
            try:
                mp3 = base.with_suffix(".mp3")
                asyncio.run(edge_tts.Communicate(text, voz, rate=f"{velocidad:+d}%").save(str(mp3)))
                self.q.put(("ok", mp3, False))
                return
            except Exception as ex:
                error = f"Sin conexión a la voz de Alexa ({type(ex).__name__})"
        if pyttsx3:
            try:
                wav = base.with_suffix(".wav")
                engine = pyttsx3.init()
                engine.save_to_file(text, str(wav))
                engine.runAndWait()
                self.q.put(("ok", wav, True))
                return
            except Exception:
                pass
        self.q.put(("error", error, False))


# ------------------------------------------------------- fondo del escenario
class Stage:
    """Luces, notas y destellos animados dentro del panel amarillo (y fondo personalizado)."""

    def __init__(self, size):
        self.size, self.t, self.notes, self.bg = size, 0.0, [], None
        self.fx = pygame.Surface(size)
        self.mask_rgb = pygame.Surface(size)
        self.mask_rgb.fill((0, 0, 0))
        pygame.draw.rect(self.mask_rgb, (255, 255, 255), (0, 0, *size), border_radius=sx(STAGE_RADIUS))
        self.mask_a = pygame.Surface(size, pygame.SRCALPHA)
        pygame.draw.rect(self.mask_a, (255, 255, 255, 255), (0, 0, *size), border_radius=sx(STAGE_RADIUS))
        self.dim = pygame.Surface(size, pygame.SRCALPHA)
        self.dim.fill((0, 0, 14, 105))
        self.dim.blit(self.mask_a, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
        self.stars = [(random.random() * size[0], random.random() * size[1] * 0.85, random.random() * 6.3)
                      for _ in range(45)]

    def set_bg(self, img):
        if img is None:
            self.bg = None
            return
        s = cover(img, self.size)
        s.blit(self.mask_a, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
        self.bg = s

    def update(self, dt, energy):
        self.t += dt
        if random.random() < dt * (0.8 + 3.5 * energy):
            self.notes.append({"x": random.random() * self.size[0], "y": self.size[1] * (0.7 + 0.3 * random.random()),
                               "vy": 30 + 40 * random.random(), "s": random.choice((5, 6, 8)), "life": 1.0,
                               "c": random.choice(((255, 90, 200), (70, 220, 255), (255, 170, 50), (175, 110, 255))),
                               "ph": random.random() * 6.3})
        for n in self.notes:
            n["y"] -= n["vy"] * dt
            n["life"] -= dt * 0.22
        self.notes = [n for n in self.notes if n["life"] > 0 and n["y"] > -40]

    def draw(self, screen, pos, energy, dim_text, animate=True):
        if self.bg:
            screen.blit(self.bg, pos)
        if dim_text:
            screen.blit(self.dim, pos)
        if not animate:
            return
        w, h = self.size
        fx, t = self.fx, self.t
        fx.fill((0, 0, 0))
        inten = 0.55 + 0.9 * energy
        for i in range(4):  # rayos de luz
            ang = 0.5 * math.sin(t * 0.7 + i * 1.7)
            col = (60, 160, 255) if i % 2 == 0 else (255, 60, 200)
            base, length = (w * (0.14 + 0.24 * i), -10), h * 1.7
            for spread, br in ((0.20, 0.10), (0.12, 0.15), (0.06, 0.22)):
                c = tuple(min(255, int(v * br * inten)) for v in col)
                pts = [base] + [(base[0] + length * math.sin(ang + s * spread), length * math.cos(ang + s * spread))
                                for s in (-1, 1)]
                pygame.draw.polygon(fx, c, pts)
        for x, y, ph in self.stars:  # destellos
            b = 0.5 + 0.5 * math.sin(t * 3 + ph)
            c = int(150 * b * inten)
            pygame.draw.circle(fx, (c, c, c), (int(x), int(y)), 2)
        for n in self.notes:  # notas flotantes
            b = min(1.0, n["life"] * 2.5) * 0.85
            c = tuple(int(v * b) for v in n["c"])
            x, y, s = int(n["x"] + 14 * math.sin(t * 1.5 + n["ph"])), int(n["y"]), n["s"]
            pygame.draw.circle(fx, c, (x, y), s)
            pygame.draw.line(fx, c, (x + s - 1, y), (x + s - 1, y - 3 * s), max(2, s // 3))
            pygame.draw.line(fx, c, (x + s - 1, y - 3 * s), (x + 3 * s, y - 2 * s), max(2, s // 3))
        fx.blit(self.mask_rgb, (0, 0), special_flags=pygame.BLEND_RGB_MULT)
        screen.blit(fx, pos, special_flags=pygame.BLEND_RGB_ADD)


# ---------------------------------------------------------------------- juego
class Game:
    def __init__(self):
        pygame.mixer.pre_init(44100, -16, 2, 2048)
        pygame.init()
        pygame.display.set_caption("Karaoke Desafío Musical")
        self.screen = pygame.display.set_mode((W, H), pygame.SCALED | pygame.RESIZABLE)
        pygame.mixer.set_num_channels(8)
        pygame.mixer.set_reserved(1)
        self.voice_ch = pygame.mixer.Channel(0)
        self._snd = None
        self.f = {s: pygame.font.SysFont("segoeui,arial,dejavusans", s, bold=b) for s, b in
                  [(18, False), (20, False), (24, False), (30, False)]}
        self._imp = {}

        self.cfg = load_config()
        self.data_dir = user_dir()
        self.alexa = Alexa()
        self.skin = self.read_img(resource_path("assets/skin_juego.jpg"), cover_size=(W, H))
        self.bg_ins = self.read_img(resource_path("assets/fondo_instrucciones.jpg"), cover_size=(W, H))
        self.stage_rect = RC(*STAGE)
        self.stage = Stage(self.stage_rect.size)
        self.load_visuals()

        self.state = "instrucciones"  # instrucciones | juego | menu
        self.first_start, self.running = True, True
        self.scores = {"rojo": 0, "azul": 0}
        self.disp = {"rojo": 0.0, "azul": 0.0}
        self.flash = {"rojo": 0.0, "azul": 0.0}
        self.history, self.popup = [], None
        self.last_digit, self.last_digit_t = None, 0
        self.timer, self.voice_len, self.announcing = 0.0, 0.0, False
        self.current_text, self._fit = "", ("", 0, [])
        self.toast_msg, self.toast_t = "", 0.0
        self.idx = 0
        self.pages, self.sel, self.scroll, self.song_edit = ["main"], 0, 0, 0
        self.editing, self.rebinding = None, None

    # ---------------------------------------------------------- recursos
    def read_img(self, path, cover_size=None, fit_size=None):
        try:
            img = pygame.image.load(str(path)).convert_alpha()
        except (pygame.error, FileNotFoundError, OSError):
            return None
        if cover_size:
            return cover(img, cover_size)
        if fit_size:
            return fit(img, fit_size)
        return img

    def img_path(self, slot, default=None):
        name = self.cfg["imagenes"].get(slot, "")
        if name and (self.data_dir / "imagenes" / name).exists():
            return self.data_dir / "imagenes" / name
        return resource_path(f"assets/{default}") if default else None

    def load_visuals(self):
        p = self.img_path("fondo")
        self.stage.set_bg(self.read_img(p) if p else None)
        self.logos = {}
        for slot, default in (("logo_izq", "logo_karaoke.png"), ("logo_der", "logo_desafios.png")):
            box = RC(*LOGO_BOX[slot])
            self.logos[slot] = self.read_img(self.img_path(slot, default), fit_size=box.size)

    def copy_into(self, src, folder):
        dest_dir = self.data_dir / folder
        dest = dest_dir / Path(src).name
        if Path(src).resolve() != dest.resolve():
            shutil.copyfile(src, dest)
        return dest.name

    def set_image(self, slot):
        p = tk_dialog("file", title=IMAGENES[slot],
                      filetypes=[("Imágenes", "*.png *.jpg *.jpeg *.jfif *.bmp *.webp"), ("Todos", "*.*")])
        if not p:
            return
        src = Path(p)
        ext = ".jpg" if src.suffix.lower() in (".jfif", ".jpe") else src.suffix.lower()
        for old in (self.data_dir / "imagenes").glob(f"{slot}.*"):
            old.unlink(missing_ok=True)
        dest = self.data_dir / "imagenes" / f"{slot}{ext}"
        try:
            shutil.copyfile(src, dest)
        except OSError:
            self.toast("No se pudo copiar la imagen")
            return
        if self.read_img(dest) is None:
            dest.unlink(missing_ok=True)
            self.cfg["imagenes"][slot] = ""
            self.toast("No se pudo abrir esa imagen")
        else:
            self.cfg["imagenes"][slot] = dest.name
            self.toast(f"{IMAGENES[slot]} cambiado")
        save_config(self.cfg)
        self.load_visuals()

    def reset_image(self, slot):
        for old in (self.data_dir / "imagenes").glob(f"{slot}.*"):
            old.unlink(missing_ok=True)
        self.cfg["imagenes"][slot] = ""
        save_config(self.cfg)
        self.load_visuals()
        self.toast(f"{IMAGENES[slot]}: original")

    def toast(self, msg, secs=3.5):
        self.toast_msg, self.toast_t = msg, secs

    # ------------------------------------------------------ canciones
    def song(self):
        return self.cfg["canciones"][min(self.idx, len(self.cfg["canciones"]) - 1)]

    def change_song(self, d):
        pygame.mixer.music.stop()
        self.voice_ch.stop()
        self.idx = (self.idx + d) % len(self.cfg["canciones"])
        self.current_text, self.timer, self.voice_len = "", 0.0, 0.0

    def speak_current(self):
        s = self.song()
        voz = self.data_dir / "voz" / s["voz"] if s["voz"] else None
        if voz and voz.exists():
            self.play_voice_file(voz, s["texto"].strip())
        elif s["texto"].strip():
            self.say(s["texto"].strip())
        else:
            self.toast("Esta canción no tiene texto ni voz (menú 8)")

    def toggle_music(self):
        if pygame.mixer.music.get_busy():
            pygame.mixer.music.stop()
            return
        s = self.song()
        track = self.data_dir / "pistas" / s["pista"] if s["pista"] else None
        if not track or not track.exists():
            self.toast("Esta canción no tiene pista MP3 (menú 8)")
            return
        try:
            pygame.mixer.music.load(str(track))
            pygame.mixer.music.play()  # siempre desde el principio
        except pygame.error:
            self.toast("No se pudo abrir esa pista")

    def add_song(self, nombre="Nueva canción"):
        self.cfg["canciones"].append({"nombre": nombre, "texto": "", "pista": "", "voz": ""})
        return len(self.cfg["canciones"]) - 1

    def import_folder(self):
        folder = tk_dialog("dir", title="Carpeta con 'NOMBRE ALEXA.mp3' y 'NOMBRE MUSICA.mp3'")
        if not folder:
            return
        groups = {}
        for p in Path(folder).iterdir():
            if p.suffix.lower() not in AUDIO_EXT:
                continue
            base, kind = split_name(p.stem)
            g = groups.setdefault(base.lower(), {"nombre": base, "pista": None, "voz": None})
            g[kind] = p
        n = 0
        for _, g in sorted(groups.items(), key=lambda kv: natural_key(kv[0])):
            try:
                pista = self.copy_into(g["pista"], "pistas") if g["pista"] else ""
                voz = self.copy_into(g["voz"], "voz") if g["voz"] else ""
            except OSError:
                continue
            found = [s for s in self.cfg["canciones"] if s["nombre"].lower() == g["nombre"].lower()]
            s = found[0] if found else self.cfg["canciones"][self.add_song(g["nombre"])]
            s["pista"], s["voz"] = pista or s["pista"], voz or s["voz"]
            n += 1
        save_config(self.cfg)
        self.toast(f"{n} canciones importadas")

    # --------------------------------------------------------- Alexa / audio
    def say(self, text, announce=False):
        if not text.strip():
            return False
        self.voice_ch.stop()
        if self.alexa.busy:
            self.toast("Alexa aún está preparando su voz…")
            return False
        self.announcing = announce
        if not announce:
            self.current_text, self.timer, self.voice_len = text, 0.0, 0.0
        return self.alexa.speak(text, self.cfg["voz"], int(self.cfg["velocidad_voz"]))

    def play_sound(self, path, announce):
        try:
            self._snd = pygame.mixer.Sound(str(path))
            self._snd.set_volume(self.cfg["volumen_voz"] / 100)
            self.voice_ch.play(self._snd)
            if not announce:
                self.voice_len = self._snd.get_length()
            return True
        except pygame.error:
            self.toast("No se pudo reproducir el audio de Alexa")
            return False

    def play_voice_file(self, path, text):
        self.voice_ch.stop()
        self.announcing = False
        self.current_text, self.timer, self.voice_len = text, 0.0, 0.0
        self.play_sound(path, False)

    # ------------------------------------------------------ calificador
    def frase_for(self, v):
        for i, (lo, hi, _) in enumerate(RANGOS):
            if lo <= v <= hi:
                return self.cfg["frases"][i]
        return ""

    def calificar(self):
        if self.popup and self.popup["roll"]:
            return
        self.popup = {"final": random.randint(0, 10), "shown": random.randint(0, 10), "t": 0.0, "roll": True,
                      "manual": False, "tick": 0}

    def press_digit(self, d):
        now = pygame.time.get_ticks()
        v = 10 if (d == 0 and self.last_digit == 1 and now - self.last_digit_t < 1200) else d
        self.last_digit, self.last_digit_t = d, now
        self.popup = {"final": v, "shown": v, "t": 0.0, "roll": False, "manual": True, "tick": 0}

    def speak_grade(self, v):
        pts = f"{v} punto" + ("" if v == 1 else "s")
        txt = self.cfg["textos"]["calificacion"].replace("{puntos}", pts).replace("{frase}", self.frase_for(v))
        self.say(txt, announce=True)

    def assign(self, team):
        p = self.popup
        if not p or p["roll"]:
            self.toast("Primero califica (tecla del calificador) o escribe un número 0-10")
            return
        self.scores[team] += p["final"]
        self.history.append((team, p["final"]))
        self.flash[team] = 1.6
        self.popup = None

    def undo(self):
        if self.history:
            team, g = self.history.pop()
            self.scores[team] -= g
            self.toast(f"Deshecho: -{g} al equipo {team}")

    # ------------------------------------------------------------- eventos
    def handle(self, e):
        if e.type == pygame.QUIT:
            self.running = False
        elif self.state == "instrucciones":
            if e.type == pygame.KEYDOWN and e.key in (pygame.K_RETURN, pygame.K_SPACE, pygame.K_KP_ENTER):
                self.state = "juego"
                if self.first_start:
                    self.first_start = False
                    self.say(self.cfg["textos"]["bienvenida"], announce=True)
            elif e.type == pygame.MOUSEBUTTONDOWN:
                self.state = "juego"
        elif self.state == "menu":
            self.handle_menu(e)
        else:
            self.handle_game(e)

    def handle_game(self, e):
        if e.type != pygame.KEYDOWN:
            return
        k, name, t = e.key, pygame.key.name(e.key), self.cfg["teclas"]
        if len(e.unicode) == 1 and e.unicode in "0123456789":
            self.press_digit(int(e.unicode))
        elif k == pygame.K_m:
            self.state, self.pages, self.sel, self.scroll = "menu", ["main"], 0, 0
        elif k in (pygame.K_ESCAPE, pygame.K_RETURN):
            self.popup = None
        elif k == pygame.K_b:
            self.say(self.cfg["textos"]["bienvenida"], announce=True)
        elif k == pygame.K_r:
            self.timer = 0.0
        elif k == pygame.K_F9:
            self.scores, self.history = {"rojo": 0, "azul": 0}, []
        elif k == pygame.K_F11:
            pygame.display.toggle_fullscreen()
        elif k == pygame.K_h:
            self.state = "instrucciones"
        else:
            act = {v: a for a, v in t.items()}.get(name)
            {"hablar": self.speak_current, "musica": self.toggle_music, "calificar": self.calificar,
             "siguiente": lambda: self.change_song(1), "anterior": lambda: self.change_song(-1),
             "rojo": lambda: self.assign("rojo"), "azul": lambda: self.assign("azul"),
             "deshacer": self.undo}.get(act, lambda: None)()

    # ---------------------------------------------------------------- menú
    def menu_page(self):
        """(título, filas[(etiqueta, acción)], centrado)"""
        c, page = self.cfg, self.pages[-1]
        back = ("VOLVER", "back")
        if page == "main":
            return "MENÚ DE CONFIGURACIÓN KARAOKE DESAFÍO MUSICAL", [
                ("SKINS Y FONDO", ("go", "skins")), ("CONFIGURAR TECLADO", ("go", "teclas")),
                ("SONIDOS Y VOZ", ("go", "sonidos")), ("LOGOTIPOS", ("go", "logos")),
                ("CALIFICADOR Y FRASES", ("go", "frases")), ("CERRAR MENÚ", "close"),
                ("CERRAR PROGRAMA", "quit"), ("EDITAR TEXTO Y AGREGAR MÚSICA", ("go", "songs"))], True
        if page == "skins":
            return "SKINS Y FONDO", [("Fondo animado", "anim"), ("Fondo del escenario (SUPR = original)", ("img", "fondo")),
                                     back], False
        if page == "teclas":
            return "CONFIGURAR TECLADO", [(lbl, ("key", a)) for a, (lbl, _) in TECLAS.items()] + [back], False
        if page == "sonidos":
            return "SONIDOS Y VOZ", [("Voz de Alexa", "voz"), ("Velocidad de la voz", "velocidad"),
                                     ("Volumen de la pista", "vol_m"), ("Volumen de Alexa", "vol_v"),
                                     ("Alexa dice la calificación", "anunciar"), ("Probar voz", "probar"), back], False
        if page == "logos":
            return "LOGOTIPOS (ENTER elegir · SUPR original)", [
                (IMAGENES["logo_izq"], ("img", "logo_izq")), (IMAGENES["logo_der"], ("img", "logo_der")), back], False
        if page == "frases":
            rows = [("Lo que dice Alexa", ("texto", "calificacion"))]
            rows += [("Frase: " + lbl, ("frase", i)) for i, (_, _, lbl) in enumerate(RANGOS)]
            return "CALIFICADOR Y FRASES  ({puntos} y {frase} en la plantilla)", rows + [back], False
        if page == "songs":
            rows = [("Texto de bienvenida", ("texto", "bienvenida"))]
            rows += [(f"{i + 1}. {s['nombre'][:26]}", ("song", i)) for i, s in enumerate(c["canciones"])]
            rows += [("+  AGREGAR CANCIÓN", "addsong"), ("Importar carpeta (ALEXA / MUSICA)", "import"), back]
            return "EDITAR TEXTO Y AGREGAR MÚSICA", rows, False
        return f"CANCIÓN {self.song_edit + 1}", [
            ("Nombre", ("sfield", "nombre")), ("Texto de Alexa", ("sfield", "texto")),
            ("Pista MP3 (música)", ("sfile", "pista")), ("Voz de Alexa MP3 (opcional)", ("sfile", "voz")),
            ("Eliminar esta canción", "delsong"), back], False

    def item_value(self, kind):
        c = self.cfg
        simple = {"anim": "Sí" if c["fondo_animado"] else "No", "anunciar": "Sí" if c["anunciar"] else "No",
                  "voz": dict(VOCES).get(c["voz"], c["voz"]), "velocidad": f"{int(c['velocidad_voz']):+d}%",
                  "vol_m": f"{c['volumen_musica']}%", "vol_v": f"{c['volumen_voz']}%"}
        if isinstance(kind, str):
            return simple.get(kind, "")
        k, key = kind
        if k == "key":
            return "Pulsa una tecla…" if self.rebinding == key else key_label(c["teclas"][key])
        if k == "img":
            return c["imagenes"][key] or "Original"
        if k in ("texto", "frase", "sfield"):
            return self.get_text(kind)
        if k == "sfile":
            return c["canciones"][self.song_edit][key] or "(vacío)"
        if k == "song":
            s = c["canciones"][key]
            return ("pista " if s["pista"] else "") + ("voz" if s["voz"] else "") or "vacía"
        return ""

    def get_text(self, t):
        k, key = t
        if k == "texto":
            return self.cfg["textos"][key]
        if k == "frase":
            return self.cfg["frases"][key]
        return self.cfg["canciones"][self.song_edit][key]

    def set_text(self, t, value):
        k, key = t
        if k == "texto":
            self.cfg["textos"][key] = value
        elif k == "frase":
            self.cfg["frases"][key] = value
        else:
            self.cfg["canciones"][self.song_edit][key] = value

    def adjust(self, kind, d):
        c = self.cfg
        if kind == "anim":
            c["fondo_animado"] = not c["fondo_animado"]
        elif kind == "anunciar":
            c["anunciar"] = not c["anunciar"]
        elif kind == "voz":
            ids = [v[0] for v in VOCES]
            c["voz"] = ids[((ids.index(c["voz"]) if c["voz"] in ids else 0) + d) % len(ids)]
        elif kind == "velocidad":
            c["velocidad_voz"] = max(-50, min(50, int(c["velocidad_voz"]) + 10 * d))
        elif kind == "vol_m":
            c["volumen_musica"] = max(0, min(100, c["volumen_musica"] + 10 * d))
        elif kind == "vol_v":
            c["volumen_voz"] = max(0, min(100, c["volumen_voz"] + 10 * d))
        save_config(c)

    def go_back(self):
        if len(self.pages) > 1:
            self.pages.pop()
            self.sel, self.scroll = 0, 0
        else:
            save_config(self.cfg)
            self.state = "juego"

    def activate(self, kind):
        if kind in ("anim", "anunciar", "voz", "velocidad", "vol_m", "vol_v"):
            self.adjust(kind, 1)
        elif kind == "back":
            self.go_back()
        elif kind == "close":
            save_config(self.cfg)
            self.state = "juego"
        elif kind == "quit":
            self.running = False
        elif kind == "probar":
            self.say(self.cfg["textos"]["bienvenida"], announce=True)
        elif kind == "import":
            self.import_folder()
        elif kind == "addsong":
            self.song_edit = self.add_song(f"Canción {len(self.cfg['canciones']) + 1}")
            save_config(self.cfg)
            self.pages.append("song")
            self.sel, self.scroll = 0, 0
            self.editing = (("sfield", "nombre"), "")  # escribir el nombre enseguida
            pygame.key.start_text_input()
        elif kind == "delsong":
            if len(self.cfg["canciones"]) > 1:
                del self.cfg["canciones"][self.song_edit]
                self.idx = min(self.idx, len(self.cfg["canciones"]) - 1)
                save_config(self.cfg)
                self.go_back()
            else:
                self.toast("Debe quedar al menos una canción")
        elif isinstance(kind, tuple):
            k, key = kind
            if k == "go":
                self.pages.append(key)
                self.sel, self.scroll = 0, 0
            elif k == "song":
                self.song_edit = key
                self.pages.append("song")
                self.sel, self.scroll = 0, 0
            elif k == "key":
                self.rebinding = key
            elif k == "img":
                self.set_image(key)
            elif k == "sfile":
                self.set_song_file(key)
            else:
                self.editing = (kind, self.get_text(kind))
                pygame.key.start_text_input()

    def set_song_file(self, field):
        p = tk_dialog("file", title="Elige un archivo de audio", filetypes=[("Audio", "*.mp3 *.wav *.ogg")])
        if not p:
            return
        try:
            self.cfg["canciones"][self.song_edit][field] = self.copy_into(p, "pistas" if field == "pista" else "voz")
            save_config(self.cfg)
            self.toast("Archivo guardado en la carpeta del juego")
        except OSError:
            self.toast("No se pudo copiar el archivo")

    def handle_menu(self, e):
        if self.editing:
            target, buf = self.editing
            if e.type == pygame.TEXTINPUT:
                self.editing = (target, buf + e.text)
            elif e.type == pygame.KEYDOWN:
                if e.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    value = buf.strip()
                    if not value and target != ("sfield", "texto"):
                        value = self.get_text(target)  # no dejar vacío lo que no puede estarlo
                    self.set_text(target, value)
                    save_config(self.cfg)
                    self.editing = None
                    pygame.key.stop_text_input()
                elif e.key == pygame.K_ESCAPE:
                    self.editing = None
                    pygame.key.stop_text_input()
                elif e.key == pygame.K_BACKSPACE:
                    self.editing = (target, buf[:-1])
                elif e.key == pygame.K_DELETE:
                    self.editing = (target, "")
                elif e.key == pygame.K_v and (e.mod & pygame.KMOD_CTRL):
                    self.editing = (target, buf + " ".join(tk_dialog("clip").split()))
            return
        if self.rebinding:
            if e.type == pygame.KEYDOWN:
                name = pygame.key.name(e.key)
                others = [v for s, v in self.cfg["teclas"].items() if s != self.rebinding]
                if e.key != pygame.K_ESCAPE:
                    if name in TECLAS_FIJAS or name in others:
                        self.toast("Esa tecla ya tiene otra función")
                    else:
                        self.cfg["teclas"][self.rebinding] = name
                        save_config(self.cfg)
                self.rebinding = None
            return
        if e.type != pygame.KEYDOWN:
            return
        title, rows, _ = self.menu_page()
        self.sel = min(self.sel, len(rows) - 1)
        kind = rows[self.sel][1]
        if e.key == pygame.K_ESCAPE or (e.key == pygame.K_m and len(self.pages) == 1):
            self.go_back()
        elif e.key == pygame.K_UP:
            self.sel = (self.sel - 1) % len(rows)
        elif e.key == pygame.K_DOWN:
            self.sel = (self.sel + 1) % len(rows)
        elif e.key in (pygame.K_LEFT, pygame.K_RIGHT) and isinstance(kind, str):
            self.adjust(kind, 1 if e.key == pygame.K_RIGHT else -1)
        elif e.key == pygame.K_DELETE and isinstance(kind, tuple):
            if kind[0] == "img":
                self.reset_image(kind[1])
            elif kind[0] == "song" and len(self.cfg["canciones"]) > 1:
                del self.cfg["canciones"][kind[1]]
                self.idx = min(self.idx, len(self.cfg["canciones"]) - 1)
                save_config(self.cfg)
        elif e.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self.activate(kind)
        elif len(e.unicode) == 1 and e.unicode in "123456789" and int(e.unicode) <= len(rows):
            self.sel = int(e.unicode) - 1
            if self.pages[-1] == "main":
                self.activate(rows[self.sel][1])

    # ---------------------------------------------------------- actualizar
    def update(self, dt):
        try:
            while True:
                msg = self.alexa.q.get_nowait()
                self.alexa.busy = False
                if msg[0] == "ok":
                    if self.play_sound(msg[1], self.announcing) and msg[2]:
                        self.toast("Sin internet: usando la voz de Windows")
                    try:
                        os.remove(msg[1])
                    except OSError:
                        pass
                else:
                    self.toast(msg[1], 6)
        except queue.Empty:
            pass
        speaking = self.voice_ch.get_busy()
        music = pygame.mixer.music.get_busy()
        if speaking and not self.announcing:  # el temporizador solo corre mientras Alexa lee el texto
            self.timer += dt
        if music:
            pygame.mixer.music.set_volume(self.cfg["volumen_musica"] / 100 * (0.25 if speaking else 1.0))
        for t in ("rojo", "azul"):  # marcador que cuenta hacia arriba
            diff, step = self.scores[t] - self.disp[t], 14 * dt
            self.disp[t] = self.scores[t] if abs(diff) <= step else self.disp[t] + math.copysign(step, diff)
            self.flash[t] = max(0.0, self.flash[t] - dt)
        p = self.popup
        if p and p["roll"]:
            p["t"] += dt
            if int(p["t"] * 18) != p["tick"]:
                p["tick"], p["shown"] = int(p["t"] * 18), random.randint(0, 10)
            if p["t"] >= 1.7:
                p["roll"], p["shown"], p["t"] = False, p["final"], 0.0
                if self.cfg["anunciar"]:
                    self.speak_grade(p["final"])
        elif p:
            p["t"] += dt
        self.stage.update(dt, 1.0 if music else (0.7 if speaking else 0.25))
        self.toast_t = max(0.0, self.toast_t - dt)

    # ------------------------------------------------------------- dibujo
    def text(self, s, size, color, center=None, topleft=None, right=None):
        img = self.f[size].render(s, True, color)
        r = img.get_rect()
        if center:
            r.center = center
        elif topleft:
            r.topleft = topleft
        elif right:
            r.midright = right
        self.screen.blit(img, r)
        return r

    def font_imp(self, size):
        size = max(8, int(size))
        if size not in self._imp:
            self._imp[size] = pygame.font.SysFont("impact,arialblack,arial", size)
        return self._imp[size]

    def outlined(self, s, font, center, color=BLANCO, w=3, outline=(0, 0, 0)):
        base, img = font.render(s, True, outline), font.render(s, True, color)
        r = img.get_rect(center=center)
        for dx in range(-w, w + 1):
            for dy in range(-w, w + 1):
                if dx * dx + dy * dy <= w * w + 1:
                    self.screen.blit(base, r.move(dx, dy))
        self.screen.blit(img, r)

    def fit_size(self, s, start, max_w):
        size = start
        while size > 10 and self.font_imp(size).size(s)[0] > max_w:
            size -= 2
        return size

    def panel(self, rect, color=(15, 15, 30), alpha=190, border=None, radius=18):
        surf = pygame.Surface(rect.size, pygame.SRCALPHA)
        pygame.draw.rect(surf, (*color, alpha), surf.get_rect(), border_radius=radius)
        self.screen.blit(surf, rect.topleft)
        if border:
            pygame.draw.rect(self.screen, border[0], rect, border[1], border_radius=radius)

    def fit_lines(self, text, max_w, max_h):
        if self._fit[0] == text:
            return self._fit[1], self._fit[2]
        size, lines = 28, [text]
        for size in range(76, 27, -4):
            lines = wrap(text, self.font_imp(size), max_w)
            if len(lines) * int(size * 1.18) <= max_h:
                break
        self._fit = (text, size, lines)
        return size, lines

    def draw_wave(self, speaking):
        t, cx, cy = pygame.time.get_ticks() / 1000, *PT(*WAVE_C)
        n, gap = 21, sx(14)
        for i in range(n):
            if speaking:
                amp = 0.18 + 0.82 * abs(math.sin(t * 7 + i * 0.55) * math.sin(t * 3.1 + i * 0.21 + 1))
                amp *= 1 - abs(i - n // 2) / (n * 0.75)
            else:
                amp = 0.07
            hgt = max(sx(10), int(sx(210) * amp))
            x = cx + (i - n // 2) * gap
            pygame.draw.line(self.screen, (0, 80, 170), (x, cy - hgt // 2), (x, cy + hgt // 2), sx(14))
            pygame.draw.line(self.screen, (80, 235, 255), (x, cy - hgt // 2), (x, cy + hgt // 2), sx(7))

    def draw_lamps(self, speaking):
        tick = pygame.time.get_ticks() // 170
        for side, (xs, y) in LAMPS.items():
            for i, x in enumerate(xs):
                j = i if side == "izq" else 2 - i
                on = (not speaking) or ((tick - j) % 3 == 0)
                c = PT(x, y)
                pygame.draw.circle(self.screen, (235, 175, 20), c, sx(52))
                pygame.draw.circle(self.screen, (150, 95, 5), c, sx(44))
                pygame.draw.circle(self.screen, (255, 45, 45) if on else (105, 15, 15), c, sx(39))
                if on:
                    pygame.draw.circle(self.screen, (255, 170, 160), (c[0] - sx(11), c[1] - sx(13)), sx(10))

    def draw_hud(self, speaking):
        s = self.screen
        for slot, img in self.logos.items():  # logotipos
            if img:
                box = RC(*LOGO_BOX[slot])
                s.blit(img, img.get_rect(center=box.center))
        sg = self.song()
        num = str(self.idx + 1)
        self.outlined(num, self.font_imp(self.fit_size(num, sx(320), sx(215))), PT(*NUM_C), BLANCO, 4, (0, 8, 45))
        name = sg["nombre"] or "..."
        self.outlined(name, self.font_imp(self.fit_size(name, sx(100), sx(690))), PT(*SUB_C), BLANCO, 3, (0, 8, 45))
        self.draw_wave(speaking)
        for team in ("rojo", "azul"):  # marcadores
            shown = int(round(self.disp[team]))
            txt = str(shown)
            col = AMARILLO if shown != self.scores[team] else BLANCO
            self.outlined(txt, self.font_imp(self.fit_size(txt, sx(150), sx(230))), PT(*SCORE_C[team]), col, 2)
            if self.flash[team] > 0 and int(self.flash[team] * 8) % 2 == 0:
                pygame.draw.rect(s, (255, 225, 60), RC(*TEAM_R[team]), 5, border_radius=sx(55))
        mm, ss = divmod(int(self.timer), 60)  # temporizador y barra
        tt = f"{mm}:{ss:02d}"
        self.outlined(tt, self.font_imp(self.fit_size(tt, sx(135), sx(175))), PT(*OVAL_C), BLANCO, 2, (0, 20, 100))
        tr = RC(*BAR)
        fw = int(tr.w * (min(1.0, self.timer / self.voice_len) if self.voice_len else 0.0))
        if fw > 0:
            fill = pygame.Rect(tr.x, tr.y, max(fw, tr.h // 2), tr.h)
            pygame.draw.rect(s, (15, 75, 235), fill, border_radius=tr.h // 2)
            hl = pygame.Rect(fill.x + 4, fill.y + 3, max(2, fill.w - 8), fill.h // 3)
            pygame.draw.rect(s, (80, 150, 255), hl, border_radius=hl.h // 2)
        self.draw_lamps(speaking)
        if speaking:  # el cartel TIEMPO brilla mientras Alexa habla
            lab = RC(*LABEL)
            g = pygame.Surface(lab.size, pygame.SRCALPHA)
            a = int(40 + 45 * (0.5 + 0.5 * math.sin(pygame.time.get_ticks() / 130)))
            pygame.draw.rect(g, (255, 235, 120, a), g.get_rect(), border_radius=sx(45))
            s.blit(g, lab.topleft)

    def draw_stage_text(self):
        area = RC(*TEXT_AREA)
        sg = self.song()
        if self.current_text:
            size, lines = self.fit_lines(self.current_text, area.w, area.h)
            lh = int(size * 1.18)
            y0 = area.centery - len(lines) * lh // 2 + lh // 2
            for i, ln in enumerate(lines):
                self.outlined(ln, self.font_imp(size), (area.centerx, y0 + i * lh), BLANCO, 3)
        else:
            self.outlined(sg["nombre"][:34] or "Canción", self.font_imp(sx(150)), (area.centerx, area.centery - 20),
                          BLANCO, 3)
            t = self.cfg["teclas"]
            self.text(f"{key_label(t['hablar'])}: Alexa habla  ·  {key_label(t['musica'])}: pista  ·  "
                      f"{key_label(t['calificar'])}: calificar  ·  M: menú  ·  H: ayuda", 24, GRIS,
                      center=(area.centerx, area.centery + 50))
        estado = "sonando" if pygame.mixer.music.get_busy() else "detenida"
        self.text(f"Canción {self.idx + 1}/{len(self.cfg['canciones'])}  ·  Pista: {estado}", 18, (200, 205, 225),
                  topleft=(self.stage_rect.x + 26, self.stage_rect.y + 14))

    def draw_popup(self):
        p = self.popup
        if not p:
            return
        s, r = self.screen, pygame.Rect(0, 0, 640, 350)
        r.center = (W // 2, H // 2 + 20)
        pygame.draw.rect(s, (20, 90, 230), r.inflate(20, 20), border_radius=22)
        for y in range(r.h):  # panel plateado
            v = 228 - int(70 * y / r.h)
            pygame.draw.line(s, (v, v, v + 4), (r.x, r.y + y), (r.right - 1, r.y + y))
        pygame.draw.rect(s, (60, 60, 70), r, 3)
        self.outlined("TU CALIFICACIÓN SOBRE 10 PTS ES...", self.font_imp(34), (r.centerx, r.y + 42),
                      (15, 15, 15), 0, (15, 15, 15))
        val = str(p["shown"])
        led = pygame.Rect(0, 0, 150 if len(val) == 1 else 220, 170)
        led.center = (r.centerx - 30, r.y + 160)
        pygame.draw.rect(s, (8, 8, 8), led, border_radius=6)
        pygame.draw.rect(s, (95, 95, 95), led, 4, border_radius=6)
        glow = (20, 120, 20) if p["roll"] else (30, 170, 30)
        self.outlined(val, self.font_imp(150), led.center, (90, 255, 90) if not p["roll"] else (60, 200, 60), 2, glow)
        self.outlined("PTS.", self.font_imp(34), (led.right + 55, led.bottom - 28), (15, 15, 15), 0, (15, 15, 15))
        if not p["roll"]:
            ph = self.frase_for(p["final"])
            size = self.fit_size(ph, 40, r.w - 50)
            self.outlined(ph, self.font_imp(size), (r.centerx, r.y + 268), (35, 35, 190), 0, (35, 35, 190))
            t = self.cfg["teclas"]
            self.text(f"{key_label(t['rojo'])} = equipo ROJO   ·   {key_label(t['azul'])} = equipo AZUL   ·   "
                      "ESC = cerrar", 20, (40, 40, 50), center=(r.centerx, r.bottom - 24))

    def draw_game(self):
        s = self.screen
        if self.skin:
            s.blit(self.skin, (0, 0))
        else:
            s.fill((10, 20, 70))
        speaking = self.voice_ch.get_busy()
        energy = 1.0 if pygame.mixer.music.get_busy() else (0.7 if speaking else 0.25)
        self.stage.draw(s, self.stage_rect.topleft, energy, bool(self.current_text) and self.stage.bg is not None,
                        self.cfg["fondo_animado"])
        self.draw_stage_text()
        self.draw_hud(speaking)
        self.draw_popup()
        if self.toast_t > 0:
            r = pygame.Rect(0, 0, self.f[24].size(self.toast_msg)[0] + 40, 36)
            r.center = (W // 2, sx(330))
            self.panel(r, (0, 0, 0), 235, (AMARILLO, 2), 12)
            self.text(self.toast_msg, 24, AMARILLO, center=r.center)

    def navy_panel(self, rect):
        glow = rect.inflate(10, 10)
        pygame.draw.rect(self.screen, (120, 85, 10), glow, 3, border_radius=40)
        self.panel(rect, (6, 16, 60), 250, ((255, 190, 30), 8), 36)

    def draw_instrucciones(self):
        if self.bg_ins:
            self.screen.blit(self.bg_ins, (0, 0))
        else:
            self.screen.fill((10, 20, 70))
        r = pygame.Rect(0, 0, 1040, 620)
        r.center = (W // 2, H // 2)
        self.navy_panel(r)
        self.outlined("CÓMO JUGAR", self.font_imp(46), (W // 2, r.y + 52), AMARILLO, 2)
        t = self.cfg["teclas"]
        lines = [
            f"{key_label(t['hablar'])}  ·  Alexa dice el texto de la canción (corre el tiempo y la barra)",
            f"{key_label(t['musica'])}  ·  Pista: empieza desde el principio; otra vez: se detiene",
            f"{key_label(t['anterior'])} / {key_label(t['siguiente'])}  ·  Canción anterior / siguiente",
            f"{key_label(t['calificar'])}  ·  Calificador al azar: Alexa dice la nota y la frase",
            "0 a 9  ·  Escribe tú la calificación (1 y luego 0 = 10)",
            f"{key_label(t['rojo'])}  ·  Suma la calificación al equipo ROJO",
            f"{key_label(t['azul'])}  ·  Suma la calificación al equipo AZUL",
            f"{key_label(t['deshacer'])}  ·  Deshace la última suma   ·   ESC cierra el calificador",
            "B bienvenida · R reinicia tiempo · F9 reinicia marcadores · F11 pantalla completa",
            "M  ·  Menú (skins, teclado, sonidos, logotipos, frases, música)",
        ]
        for i, ln in enumerate(lines):
            self.text(ln, 24, BLANCO, topleft=(r.x + 50, r.y + 100 + i * 42))
        self.outlined("Pulsa ENTER para comenzar", self.font_imp(36), (W // 2, r.bottom - 38), VERDE, 2)

    def draw_menu(self):
        self.draw_game()
        shade = pygame.Surface((W, H), pygame.SRCALPHA)
        shade.fill((0, 0, 0, 150))
        self.screen.blit(shade, (0, 0))
        title, rows, centered = self.menu_page()
        self.sel = min(self.sel, len(rows) - 1)
        vis = min(len(rows), 8)
        if self.sel < self.scroll:
            self.scroll = self.sel
        elif self.sel >= self.scroll + vis:
            self.scroll = self.sel - vis + 1
        pw, ph = 660, 110 + vis * 68 + 14
        panel = pygame.Rect((W - pw) // 2, max(8, (H - ph) // 2 - 12), pw, ph)
        self.navy_panel(panel)
        for i, ln in enumerate(wrap(title, self.font_imp(26), pw - 80)[:2]):
            self.outlined(ln, self.font_imp(26), (panel.centerx, panel.y + 38 + i * 32), BLANCO, 0, (6, 16, 60))
        for row, idx in enumerate(range(self.scroll, self.scroll + vis)):
            label, kind = rows[idx]
            rr = pygame.Rect(panel.x + 24, panel.y + 90 + row * 68, pw - 48, 56)
            on = idx == self.sel
            pygame.draw.rect(self.screen, (14, 34, 100) if on else (8, 22, 70), rr, border_radius=28)
            pygame.draw.rect(self.screen, (255, 190, 30) if on else (40, 150, 240), rr, 3, border_radius=28)
            c = (rr.x + 34, rr.centery)
            pygame.draw.circle(self.screen, (255, 200, 40), c, 25)
            pygame.draw.circle(self.screen, (200, 25, 30), c, 22)
            self.outlined(str(idx + 1), self.font_imp(24), c, BLANCO, 1, (90, 0, 0))
            val = self.item_value(kind)
            if centered:
                self.outlined(label, self.font_imp(self.fit_size(label, 30, rr.w - 120)),
                              (rr.centerx + 20, rr.centery), AMARILLO, 1, (60, 40, 0))
            else:
                size = self.fit_size(label, 26, rr.w - 80 - (260 if val else 0))
                self.outlined(label, self.font_imp(size), (rr.x + 70 + self.font_imp(size).size(label)[0] // 2,
                                                          rr.centery), AMARILLO, 1, (60, 40, 0))
                if val:
                    val = val if len(val) <= 28 else val[:25] + "…"
                    self.text(val, 24, (150, 225, 255) if on else BLANCO, right=(rr.right - 20, rr.centery))
        hint = "↑↓ elegir · ENTER aceptar · ←→ cambiar · SUPR borrar/original · ESC volver"
        self.text(hint, 18, BLANCO, center=(W // 2, min(H - 12, panel.bottom + 18)))
        if self.editing:
            ed = pygame.Rect(0, 0, 1000, 100)
            ed.center = (W // 2, H - 110)
            self.panel(ed, (0, 0, 0), 250, (VERDE, 3), 12)
            for i, ln in enumerate(wrap(self.editing[1] + "|", self.f[24], ed.w - 30)[-2:]):
                self.text(ln, 24, BLANCO, topleft=(ed.x + 15, ed.y + 12 + i * 32))
            self.text("ENTER guardar · ESC cancelar · SUPR borra todo · Ctrl+V pegar", 18, GRIS, topleft=(ed.x, ed.y - 24))
        if self.toast_t > 0:
            r = pygame.Rect(0, 0, self.f[24].size(self.toast_msg)[0] + 40, 36)
            r.center = (W // 2, H - 30)
            self.panel(r, (0, 0, 0), 235, (AMARILLO, 2), 12)
            self.text(self.toast_msg, 24, AMARILLO, center=r.center)

    def draw(self):
        {"instrucciones": self.draw_instrucciones, "juego": self.draw_game, "menu": self.draw_menu}[self.state]()

    def run(self):
        clock = pygame.time.Clock()
        while self.running:
            dt = clock.tick(60) / 1000
            for e in pygame.event.get():
                self.handle(e)
            self.update(dt)
            self.draw()
            pygame.display.flip()
        save_config(self.cfg)
        pygame.quit()


if __name__ == "__main__":
    Game().run()
