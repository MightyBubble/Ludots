from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image, ImageDraw

here = Path(__file__).resolve().parent
source = here / 'results-10000-normal-fix'
output = here / 'normal-evidence'
report = []
crops = Image.new('RGB', (1280, 3 * 300), '#202830')
draw = ImageDraw.Draw(crops)
for pose in range(3):
    baseline = np.array(Image.open(source / f'pose-{pose}-baseline.png').convert('RGB'))
    preskin = np.array(Image.open(source / f'pose-{pose}-preskin.png').convert('RGB'))
    difference = np.abs(baseline.astype(np.int16) - preskin.astype(np.int16))
    maximum = difference.max(axis=2)
    changed = maximum > 0
    high = maximum > 8
    signed_a = baseline.astype(np.int16)
    signed_b = preskin.astype(np.int16)
    red_blue_swap = (((signed_a[:, :, 0] - signed_a[:, :, 2]) > 20) & ((signed_b[:, :, 2] - signed_b[:, :, 0]) > 20)) | (((signed_a[:, :, 2] - signed_a[:, :, 0]) > 20) & ((signed_b[:, :, 0] - signed_b[:, :, 2]) > 20))
    background = np.array([34, 40, 48])
    ba, bb = (baseline == background).all(axis=2), (preskin == background).all(axis=2)
    edges = np.zeros(changed.shape, np.uint8)
    for image in [baseline, preskin]:
        gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
        edges |= cv2.Canny(gray, 50, 100)
    distances = cv2.distanceTransform((edges == 0).astype(np.uint8), cv2.DIST_L2, 5)
    count, labels, components, centers = cv2.connectedComponentsWithStats(high.astype(np.uint8), 8)
    areas = components[1:, cv2.CC_STAT_AREA]
    coords = np.argwhere(maximum == maximum.max())
    top = []
    for y, x in coords[:10]:
        top.append({'x': int(x), 'y': int(y), 'baseline_rgb': baseline[y, x].tolist(),
                    'preskin_rgb': preskin[y, x].tolist(), 'max_error': int(maximum[y, x]),
                    'distance_to_image_edge_pixels': float(distances[y, x])})
    ys, xs = np.where(high)
    nearest = []
    for y, x in zip(ys, xs):
        neighbors = baseline[max(0, y - 1):min(baseline.shape[0], y + 2), max(0, x - 1):min(baseline.shape[1], x + 2)].astype(np.int16)
        nearest.append(int(np.abs(neighbors - preskin[y, x]).max(axis=2).min()))
    nearest = np.array(nearest)
    histogram, _, _ = np.histogram2d(ys, xs, bins=[np.linspace(0, baseline.shape[0], 5), np.linspace(0, baseline.shape[1], 5)])
    item = {'pose': pose, 'normalized_time': [0, .25, .5][pose],
        'background_xor': int((ba ^ bb).sum()), 'background_baseline': int(ba.sum()), 'background_preskin': int(bb.sum()),
        'changed_pixels': int(changed.sum()), 'over_8_pixels': int(high.sum()), 'max_error': int(maximum.max()),
        'mean_rgb_error': float(difference.mean()), 'max_error_locations': top,
        'over_8_red_blue_surface_swaps': int((red_blue_swap & high).sum()),
        'over_100_pixels': int((maximum > 100).sum()),
        'canny_definition': 'grayscale thresholds 50/100, union of baseline and preskin',
        'over_8_within_1px_image_edge': int((distances[high] <= 1.01).sum()),
        'over_8_within_2px_image_edge': int((distances[high] <= 2.01).sum()),
        'over_8_max_edge_distance': float(distances[high].max()),
        'over_8_components': int(count - 1), 'largest_component': int(areas.max()),
        'components_with_area_1': int((areas == 1).sum()),
        'components_with_area_at_most_4': int((areas <= 4).sum()),
        'pixels_in_components_area_at_most_4': int(areas[areas <= 4].sum()),
        'over_8_matching_a_baseline_3x3_neighbor_within_8': int((nearest <= 8).sum()),
        'over_8_location_histogram_4x4_top_to_bottom': histogram.astype(int).tolist()}
    report.append(item)
    y, x = coords[0]
    x0 = max(0, min(int(x) - 40, baseline.shape[1] - 80))
    y0 = max(0, min(int(y) - 32, baseline.shape[0] - 64))
    for column, image in enumerate([baseline, preskin, np.minimum(difference * 8, 255).astype(np.uint8)]):
        crop = Image.fromarray(image[y0:y0 + 64, x0:x0 + 80]).resize((400, 256), Image.Resampling.NEAREST)
        crops.paste(crop, (column * 420, pose * 300 + 25))
    draw.text((0, pose * 300 + 5), f'Pose {pose}: baseline / corrected preskin / difference x8; crop x={x0}, y={y0}; nearest pixel scaling 5x', fill='white')

(output / 'corrected-residual-analysis.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
crops.save(output / 'corrected-max-error-crops.png')
print(json.dumps(report, indent=2))
