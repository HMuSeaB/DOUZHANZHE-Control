export default function OverrideSlider({
  label, desc, value, min, max, step, unit, displayValue, disabled, action,
  set, onEnable, onClear, onChange,
}) {
  return (
    <div className={"override-slider" + (set ? "" : " unset")}>
      <div className="switch-row">
        <span className="k">{label}</span>
        <button
          className={"switch-track" + (set ? " on" : " off")}
          type="button"
          disabled={disabled}
          onClick={() => (set ? onClear?.() : onEnable?.())}
          aria-label={(set ? "已设置" : "未设置") + " " + label}
        >
          <span className="thumb"></span>
        </button>
      </div>
      {set && (
        <div className="slider-group">
          <div className="slider-label">
            <span className="desc">{desc || label}</span>
            <span className="v">{displayValue ?? value}<span className="u">{displayValue ? "" : unit}</span>{action}</span>
          </div>
          <input
            type="range"
            className="slider-track"
            min={min}
            max={max}
            step={step}
            value={value}
            disabled={disabled}
            onChange={e => onChange(Number(e.target.value))}
          />
        </div>
      )}
    </div>
  );
}
