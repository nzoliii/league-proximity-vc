import cv2
import numpy as np
import mss
import time
import json
import socket
import argparse
from ultralytics import YOLO

# Arguments from server
parser = argparse.ArgumentParser(description="LOLProximityVC detection script")
parser.add_argument("--model",       default="model.pt", help="YOLO model path")
parser.add_argument("--fps",         type=int,   default=30,    help="Target FPS")
parser.add_argument("--confidence",  type=float, default=0.5,   help="Detection confidence threshold")
parser.add_argument("--countdown",   type=int,   default=5,     help="Countdown before starting")
parser.add_argument("--map-width",   type=int,   default=14870, help="League map width in units")
parser.add_argument("--map-height",  type=int,   default=14870, help="League map height in units")
parser.add_argument("--bridge-port", type=int,   default=7778,  help="Local UDP port to send positions to")
args = parser.parse_args()

# Setup
model      = YOLO(args.model)
FRAME_TIME = 1 / args.fps

_bridge_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
_bridge_addr = ("127.0.0.1", args.bridge_port)


def send_position(champion_name, game_x, game_y):
    payload = json.dumps({
        "champion": champion_name,
        "x":        game_x,
        "y":        game_y
    }).encode("utf-8")
    _bridge_sock.sendto(payload, _bridge_addr)


def find_minimap(img, prev_box=None):
    if prev_box:
        return prev_box
    gray = cv2.cvtColor(img, cv2.COLOR_BGRA2GRAY)
    _, thresh = cv2.threshold(gray, 30, 255, cv2.THRESH_BINARY_INV)
    contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    best, max_area = None, 0
    h, w = img.shape[:2]
    for cnt in contours:
        x, y, cw, ch = cv2.boundingRect(cnt)
        area = cw * ch
        if area > max_area and x > w // 2 and y > h // 2:
            max_area = area
            best = (x, y, cw, ch)
    return best


def minimap_to_game_coords(norm_x, norm_y):
    game_x = norm_x * args.map_width
    game_y = (1.0 - norm_y) * args.map_height
    return (round(game_x), round(game_y))


def get_icon_color(minimap, x1, y1, x2, y2):
    patch = minimap[y1:y2, x1:x2]
    if patch.size == 0:
        return (0, 255, 0)
    avg = cv2.mean(patch)[:3]
    return (int(avg[0]), int(avg[1]), int(avg[2]))


def draw_labeled_text(img, text, pos, bg_color, font_scale=0.4, thickness=1):
    font = cv2.FONT_HERSHEY_SIMPLEX
    (tw, th), baseline = cv2.getTextSize(text, font, font_scale, thickness)
    x, y = pos
    cv2.rectangle(img, (x, y - th - baseline), (x + tw, y + baseline), bg_color, -1)
    cv2.putText(img, text, (x, y), font, font_scale, (255, 255, 255), thickness)


def run():
    for i in range(args.countdown, 0, -1):
        print(f"[detection] Starting in {i}...")
        time.sleep(1)
    print(f"[detection] Go!")
    print(f"[detection] Model: {args.model} | FPS: {args.fps} | Confidence: {args.confidence}")
    print(f"[detection] Sending positions to 127.0.0.1:{args.bridge_port}")

    with mss.mss() as sct:
        monitor     = sct.monitors[1]
        minimap_box = None

        while True:
            start = time.time()
            frame = np.array(sct.grab(monitor))

            minimap_box = find_minimap(frame, minimap_box)

            if minimap_box:
                x, y, w, h = minimap_box
                minimap = np.ascontiguousarray(frame[y:y+h, x:x+w, :3])
                overlay = np.zeros_like(minimap)

                results = model(minimap, verbose=False)[0]

                for box in results.boxes:
                    x1, y1, x2, y2 = map(int, box.xyxy[0])
                    champ_name      = model.names[int(box.cls[0])]
                    conf            = float(box.conf[0])

                    if conf > args.confidence:
                        cx     = (x1 + x2) / 2
                        cy     = (y1 + y2) / 2
                        norm_x = cx / minimap.shape[1]
                        norm_y = cy / minimap.shape[0]
                        game_x, game_y = minimap_to_game_coords(norm_x, norm_y)

                        send_position(champ_name, game_x, game_y)

                        color = get_icon_color(minimap, x1, y1, x2, y2)

                        for target in (minimap, overlay):
                            cv2.rectangle(target, (x1, y1), (x2, y2), color, 2)
                            draw_labeled_text(target, f"{champ_name} {conf:.0%}",
                                              (x1, y1 - 22), color)
                            draw_labeled_text(target, f"({game_x}, {game_y})",
                                              (x1, y1 - 8), color)

                cv2.imshow("Minimap", minimap)
                cv2.imshow("Overlay", overlay)

            if cv2.waitKey(1) & 0xFF == ord('q'):
                break

            elapsed    = time.time() - start
            sleep_time = FRAME_TIME - elapsed
            if sleep_time > 0:
                time.sleep(sleep_time)

    cv2.destroyAllWindows()
    _bridge_sock.close()


if __name__ == "__main__":
    run()