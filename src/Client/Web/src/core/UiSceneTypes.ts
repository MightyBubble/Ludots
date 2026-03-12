export interface UiScenePayload {
  kind: string;
  version: number;
  viewportWidth: number;
  viewportHeight: number;
  root: UiSceneNode | null;
}

export interface UiSceneNode {
  id: number;
  kind: string;
  tagName: string;
  elementId: string | null;
  classNames: string[];
  textContent: string | null;
  imageSource: string | null;
  style: UiSceneStyle;
  layoutRect: UiRect;
  scrollOffsetX: number;
  scrollOffsetY: number;
  scrollContentWidth: number;
  scrollContentHeight: number;
  children: UiSceneNode[];
}

export interface UiSceneStyle {
  display: string;
  overflow: string;
  flexDirection: string;
  justifyContent: string;
  alignItems: string;
  alignContent: string;
  flexWrap: string;
  direction: string;
  textAlign: string;
  objectFit: string;
  width: string;
  height: string;
  minWidth: string;
  minHeight: string;
  maxWidth: string;
  maxHeight: string;
  gap: number;
  rowGap: number;
  columnGap: number;
  margin: UiThickness;
  padding: UiThickness;
  imageSlice: UiThickness;
  borderWidth: number;
  borderRadius: number;
  outlineWidth: number;
  zIndex: number;
  backgroundColor: string;
  backgroundGradient: UiGradientPayload | null;
  borderColor: string;
  outlineColor: string;
  boxShadow: UiShadowPayload | null;
  filterBlurRadius: number;
  backdropBlurRadius: number;
  color: string;
  textShadow: UiShadowPayload | null;
  fontSize: number;
  fontFamily: string | null;
  bold: boolean;
  whiteSpace: string;
  transform: string;
  opacity: number;
  visible: boolean;
  clipContent: boolean;
}

export interface UiRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface UiThickness {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

export interface UiShadowPayload {
  offsetX: number;
  offsetY: number;
  blurRadius: number;
  spreadRadius: number;
  color: string;
}

export interface UiGradientPayload {
  angleDegrees: number;
  stops: UiGradientStopPayload[];
}

export interface UiGradientStopPayload {
  position: number;
  color: string;
}
